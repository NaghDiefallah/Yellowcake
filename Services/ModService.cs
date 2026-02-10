using LiteDB;
using Newtonsoft.Json;
using Octokit;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Yellowcake.Models;
using FileMode = System.IO.FileMode;

namespace Yellowcake.Services;

public class ModService
{
    private readonly DatabaseService _db;
    private readonly InstallService _installService;
    private readonly HttpClient _http;
    private readonly GitHubClient _gh;

    public ModService(DatabaseService db, InstallService installService, HttpClient http, GitHubClient gh)
    {
        _db = db;
        _installService = installService;
        _http = http;
        _gh = gh;
    }

    public List<Mod> GetInstalledMods()
    {
        return _db.GetAll<Mod>("addons");
    }

    public bool IsBepInExInstalled()
    {
        var gamePath = _db.GetSetting("GamePath");
        if (string.IsNullOrEmpty(gamePath)) return false;

        var gameDir = Path.GetDirectoryName(gamePath);
        if (string.IsNullOrEmpty(gameDir)) return false;

        return Directory.Exists(Path.Combine(gameDir, "BepInEx"));
    }

    public async Task DownloadAndInstallModAsync(
        Mod mod,
        IProgress<double> progress,
        List<Mod> allMods,
        CancellationToken ct,
        string? expectedHash = null)
    {
        if (mod == null) throw new ArgumentNullException(nameof(mod));

        var downloadUrl = mod.DownloadUrl;
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            throw new InvalidOperationException($"No download URL available for {mod.Name}");
        }

        if (GoogleDriveHelper.IsGoogleDriveUrl(downloadUrl))
        {
            Log.Information("Converting Google Drive URL for {ModName}", mod.Name);
            downloadUrl = GoogleDriveHelper.ConvertToDirectDownloadUrl(downloadUrl);
            Log.Debug("Converted URL: {Url}", downloadUrl);
        }

        var (isValid, error) = mod.Validate();
        if (!isValid)
        {
            throw new InvalidOperationException($"Mod validation failed: {error}");
        }

        Log.Information("Starting download: {ModName} v{Version} from {Url}", mod.Name, mod.Version, downloadUrl);

        var tempFile = Path.Combine(Path.GetTempPath(), $"{mod.Id}_{Guid.NewGuid()}.tmp");

        try
        {
            await DownloadFileWithProgressAsync(downloadUrl, tempFile, progress, ct);

            var fileType = DetectFileType(tempFile);
            
            Log.Information("Detected file type: {FileType} for {ModName}", fileType, mod.Name);

            switch (fileType)
            {
                case FileType.Dll:
                    Log.Information("Installing raw DLL file: {ModName}", mod.Name);
                    await InstallRawDllAsync(mod, tempFile, ct);
                    break;

                case FileType.Archive:
                    Log.Information("Installing from archive: {ModName}", mod.Name);
                    
                    var hashToVerify = expectedHash ?? mod.EffectiveHash;
                    if (mod.ShouldVerifyHash && !string.IsNullOrWhiteSpace(hashToVerify))
                    {
                        Log.Information("Verifying hash for {ModName}", mod.Name);
                        var actualHash = await VerifyHashAsync(tempFile, hashToVerify);
                        
                        if (actualHash == "skipped")
                        {
                            Log.Debug("Hash verification skipped for {ModName}", mod.Name);
                        }
                    }

                    await _installService.InstallModAsync(mod, tempFile, ct);
                    break;

                case FileType.Invalid:
                default:
                    throw new InvalidOperationException(
                        $"Downloaded file is not a valid archive or DLL. " +
                        $"The download may have failed or the link may be incorrect.\n\n" +
                        $"If this is a Google Drive file, it may require different sharing settings.");
            }

            mod.MarkAsInstalled(mod.Version, mod.Hash);

            Log.Information("Successfully installed {ModName} v{Version}", mod.Name, mod.Version);
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to delete temp file: {TempFile}", tempFile);
            }
        }
    }

    private enum FileType
    {
        Invalid,
        Archive,
        Dll
    }

    private FileType DetectFileType(string filePath)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            var buffer = new byte[4];
            fs.Read(buffer, 0, 4);

            // ZIP magic: PK (0x50 0x4B)
            if (buffer[0] == 0x50 && buffer[1] == 0x4B)
            {
                Log.Debug("Detected ZIP archive");
                return FileType.Archive;
            }

            // RAR magic: Rar! (0x52 0x61 0x72 0x21)
            if (buffer[0] == 0x52 && buffer[1] == 0x61 && buffer[2] == 0x72 && buffer[3] == 0x21)
            {
                Log.Debug("Detected RAR archive");
                return FileType.Archive;
            }

            // 7z magic: 7z (0x37 0x7A 0xBC 0xAF)
            if (buffer[0] == 0x37 && buffer[1] == 0x7A && buffer[2] == 0xBC && buffer[3] == 0xAF)
            {
                Log.Debug("Detected 7z archive");
                return FileType.Archive;
            }

            // GZIP magic: (0x1F 0x8B)
            if (buffer[0] == 0x1F && buffer[1] == 0x8B)
            {
                Log.Debug("Detected GZIP archive");
                return FileType.Archive;
            }

            // MZ header (Windows PE - DLL/EXE) (0x4D 0x5A)
            if (buffer[0] == 0x4D && buffer[1] == 0x5A)
            {
                Log.Information("Detected Windows PE file (DLL/EXE)");
                
                // Read more to check if it's actually a DLL
                // PE files have "PE\0\0" signature at offset 0x3C
                fs.Seek(0x3C, SeekOrigin.Begin);
                var peOffset = new byte[4];
                fs.Read(peOffset, 0, 4);
                var peHeaderOffset = BitConverter.ToInt32(peOffset, 0);
                
                if (peHeaderOffset > 0 && peHeaderOffset < fs.Length - 4)
                {
                    fs.Seek(peHeaderOffset, SeekOrigin.Begin);
                    var peSignature = new byte[4];
                    fs.Read(peSignature, 0, 4);
                    
                    if (peSignature[0] == 0x50 && peSignature[1] == 0x45) // "PE\0\0"
                    {
                        Log.Information("Confirmed valid PE file - treating as DLL");
                        return FileType.Dll;
                    }
                }
                
                Log.Warning("MZ header found but not a valid PE file");
                return FileType.Invalid;
            }

            Log.Warning("Unknown file type. Magic bytes: {Bytes}", BitConverter.ToString(buffer));
            return FileType.Invalid;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to detect file type");
            return FileType.Invalid;
        }
    }

    private async Task InstallRawDllAsync(Mod mod, string dllPath, CancellationToken ct)
    {
        try
        {
            var gamePath = _db.GetSetting("GamePath");
            if (string.IsNullOrEmpty(gamePath))
            {
                throw new InvalidOperationException("Game path not set");
            }

            var gameDir = Path.GetDirectoryName(gamePath);
            if (string.IsNullOrEmpty(gameDir))
            {
                throw new InvalidOperationException("Invalid game path");
            }

            var pluginsDir = Path.Combine(gameDir, "BepInEx", "plugins");
            Directory.CreateDirectory(pluginsDir);

            var fileName = $"{mod.Id}.dll";
            var targetPath = Path.Combine(pluginsDir, fileName);

            Log.Information("Installing raw DLL to: {Path}", targetPath);

            File.Copy(dllPath, targetPath, overwrite: true);

            Log.Information("Successfully installed raw DLL: {ModName}", mod.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to install raw DLL for {ModName}", mod.Name);
            throw;
        }
    }

    public void ToggleMod(string modId, bool isEnabled)
    {
        if (string.IsNullOrWhiteSpace(modId))
        {
            throw new ArgumentNullException(nameof(modId));
        }

        var gamePath = _db.GetSetting("GamePath");
        if (string.IsNullOrEmpty(gamePath))
        {
            throw new InvalidOperationException("Game path not set");
        }

        var gameDir = Path.GetDirectoryName(gamePath);
        if (string.IsNullOrEmpty(gameDir))
        {
            throw new InvalidOperationException("Invalid game path");
        }

        var pluginsDir = Path.Combine(gameDir, "BepInEx", "plugins");
        
        if (!Directory.Exists(pluginsDir))
        {
            throw new DirectoryNotFoundException($"Plugins directory not found: {pluginsDir}");
        }

        var modPath = Path.Combine(pluginsDir, modId);
        var modDll = Path.Combine(pluginsDir, $"{modId}.dll");

        if (Directory.Exists(modPath))
        {
            var targetPath = isEnabled 
                ? modPath 
                : modPath + ".disabled";

            if (isEnabled)
            {
                if (Directory.Exists(modPath + ".disabled"))
                {
                    Directory.Move(modPath + ".disabled", modPath);
                    Log.Information("Enabled mod folder: {ModId}", modId);
                }
            }
            else
            {
                if (Directory.Exists(modPath))
                {
                    Directory.Move(modPath, targetPath);
                    Log.Information("Disabled mod folder: {ModId}", modId);
                }
            }
        }
        else if (File.Exists(modDll))
        {
            var targetPath = isEnabled 
                ? modDll 
                : modDll + ".disabled";

            if (isEnabled)
            {
                if (File.Exists(modDll + ".disabled"))
                {
                    File.Move(modDll + ".disabled", modDll);
                    Log.Information("Enabled mod DLL: {ModId}", modId);
                }
            }
            else
            {
                if (File.Exists(modDll))
                {
                    File.Move(modDll, targetPath);
                    Log.Information("Disabled mod DLL: {ModId}", modId);
                }
            }
        }
        else
        {
            Log.Warning("Mod not found for toggle: {ModId}", modId);
            throw new FileNotFoundException($"Mod not found: {modId}");
        }
    }

    public void DeleteMod(string modId)
    {
        if (string.IsNullOrWhiteSpace(modId))
        {
            throw new ArgumentNullException(nameof(modId));
        }

        var gamePath = _db.GetSetting("GamePath");
        if (string.IsNullOrEmpty(gamePath))
        {
            throw new InvalidOperationException("Game path not set");
        }

        var gameDir = Path.GetDirectoryName(gamePath);
        if (string.IsNullOrEmpty(gameDir))
        {
            throw new InvalidOperationException("Invalid game path");
        }

        Log.Information("Deleting mod: {ModId}", modId);

        var pluginsDir = Path.Combine(gameDir, "BepInEx", "plugins");
        var modFolder = Path.Combine(pluginsDir, modId);
        var modDll = Path.Combine(pluginsDir, $"{modId}.dll");
        var modDllDisabled = modDll + ".disabled";
        var modFolderDisabled = modFolder + ".disabled";

        bool deleted = false;

        if (Directory.Exists(modFolder))
        {
            Directory.Delete(modFolder, true);
            Log.Information("Deleted mod folder: {Path}", modFolder);
            deleted = true;
        }

        if (Directory.Exists(modFolderDisabled))
        {
            Directory.Delete(modFolderDisabled, true);
            Log.Information("Deleted disabled mod folder: {Path}", modFolderDisabled);
            deleted = true;
        }

        if (File.Exists(modDll))
        {
            File.Delete(modDll);
            Log.Information("Deleted mod DLL: {Path}", modDll);
            deleted = true;
        }

        if (File.Exists(modDllDisabled))
        {
            File.Delete(modDllDisabled);
            Log.Information("Deleted disabled mod DLL: {Path}", modDllDisabled);
            deleted = true;
        }

        var voicePackPath = Path.Combine(pluginsDir, "WSOYappinator", "audio", modId);
        if (Directory.Exists(voicePackPath))
        {
            Directory.Delete(voicePackPath, true);
            Log.Information("Deleted voice pack: {Path}", voicePackPath);
            deleted = true;
        }

        var missionsPath = Path.Combine(gameDir, "Missions", modId);
        if (Directory.Exists(missionsPath))
        {
            Directory.Delete(missionsPath, true);
            Log.Information("Deleted mission: {Path}", missionsPath);
            deleted = true;
        }

        if (!deleted)
        {
            Log.Warning("Mod not found for deletion: {ModId}", modId);
            throw new FileNotFoundException($"Mod not found: {modId}");
        }

        Log.Information("Successfully deleted mod: {ModId}", modId);
    }

    public static bool HasUpdate(Mod installed, Mod remote)
    {
        if (installed == null || remote == null) return false;

        try
        {
            var installedVersionStr = installed.Version;
            var remoteVersionStr = remote.Version;

            var installedVer = System.Version.Parse(Mod.CleanVersion(installedVersionStr));
            var remoteVer = System.Version.Parse(Mod.CleanVersion(remoteVersionStr));

            return remoteVer > installedVer;
        }
        catch
        {
            var installedVersionStr = installed.Version;
            var remoteVersionStr = remote.Version;
            return !string.Equals(installedVersionStr, remoteVersionStr, StringComparison.OrdinalIgnoreCase);
        }
    }

    public async Task<string> VerifyHashAsync(string filePath, string? expectedHash)
    {
        if (string.IsNullOrWhiteSpace(expectedHash) || 
            expectedHash.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning("No hash provided for verification, skipping");
            return "skipped";
        }

        try
        {
            var cleanExpectedHash = expectedHash
                .Replace("sha256:", "", StringComparison.OrdinalIgnoreCase)
                .Trim()
                .ToLowerInvariant();

            await using var stream = File.OpenRead(filePath);
            using var sha256 = SHA256.Create();
            var hashBytes = await sha256.ComputeHashAsync(stream);
            var actualHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

            if (actualHash.Equals(cleanExpectedHash, StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("Hash verification passed: {Hash}", actualHash);
                return actualHash;
            }

            Log.Warning("Hash mismatch! Expected: {Expected}, Actual: {Actual}", 
                cleanExpectedHash, actualHash);
            throw new System.Security.SecurityException($"Hash verification failed. Expected: {cleanExpectedHash}, Actual: {actualHash}");
        }
        catch (Exception ex) when (ex is not System.Security.SecurityException)
        {
            Log.Error(ex, "Hash verification error");
            throw new InvalidDataException("Failed to verify file integrity", ex);
        }
    }

    public async Task<bool> ValidateDependenciesAsync(Mod mod, List<Mod> installedMods)
    {
        var modDependencies = mod.Dependencies;
        
        if (modDependencies == null || !modDependencies.Any())
            return true;

        var missing = new List<string>();

        foreach (var depId in modDependencies)
        {
            var isInstalled = installedMods.Any(m => 
                string.Equals(m.Id, depId, StringComparison.OrdinalIgnoreCase));

            if (!isInstalled)
            {
                missing.Add(depId);
            }
        }

        if (missing.Any())
        {
            Log.Warning("Missing dependencies for {Mod}: {Dependencies}", 
                mod.Name, string.Join(", ", missing));
            return false;
        }

        return true;
    }

    public List<Mod> DetectConflicts(Mod mod, List<Mod> installedMods)
    {
        var conflicts = new List<Mod>();
        var modConflicts = mod.Conflicts;

        if (modConflicts?.Any() == true)
        {
            foreach (var conflictId in modConflicts)
            {
                var conflictingMod = installedMods.FirstOrDefault(m => 
                    string.Equals(m.Id, conflictId, StringComparison.OrdinalIgnoreCase));

                if (conflictingMod != null)
                {
                    conflicts.Add(conflictingMod);
                }
            }
        }

        return conflicts;
    }

    public async Task<ModValidationResult> ValidateModAsync(Mod mod, List<Mod> installedMods)
    {
        var result = new ModValidationResult
        {
            IsValid = true,
            Mod = mod
        };

        var downloadUrl = mod.DownloadUrl;
        
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            result.IsValid = false;
            result.Errors.Add("No download URL available");
            return result;
        }

        if (!await ValidateDependenciesAsync(mod, installedMods))
        {
            result.HasMissingDependencies = true;
            var modDependencies = mod.Dependencies;
            
            result.MissingDependencies = modDependencies
                .Where(depId => !installedMods.Any(m => 
                    string.Equals(m.Id, depId, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            result.Warnings.Add($"Missing {result.MissingDependencies.Count} dependencies");
        }

        var conflicts = DetectConflicts(mod, installedMods);
        if (conflicts.Any())
        {
            result.HasConflicts = true;
            result.ConflictingMods = conflicts;
            result.Warnings.Add($"Conflicts with {conflicts.Count} installed mod(s)");
        }

        return result;
    }

    private async Task DownloadFileWithProgressAsync(
        string url, 
        string destinationPath, 
        IProgress<double> progress, 
        CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            
            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType != null && contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                Log.Warning("Received HTML instead of file. URL may need conversion: {Url}", url);
                
                var html = await response.Content.ReadAsStringAsync(ct);
                
                if (GoogleDriveHelper.IsVirusScanWarning(html))
                {
                    Log.Warning("Google Drive virus warning detected, attempting confirmation download");
                    
                    var fileId = GoogleDriveHelper.ExtractFileId(url);
                    if (!string.IsNullOrEmpty(fileId))
                    {
                        var confirmUrl = $"https://drive.usercontent.google.com/download?id={fileId}&export=download&confirm=t";
                        Log.Information("Retrying with confirmation URL: {Url}", confirmUrl);
                        
                        await DownloadFileWithProgressAsync(confirmUrl, destinationPath, progress, ct);
                        return;
                    }
                    
                    throw new InvalidOperationException(
                        "Google Drive file requires confirmation but could not extract file ID. " +
                        "Please use a different hosting service or share the file as 'Anyone with the link'.");
                }
                
                throw new InvalidOperationException(
                    "Received HTML page instead of file. The download link may be incorrect or require authentication.");
            }

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            Log.Debug("Downloading {Bytes} bytes from {Url}", totalBytes, url);

            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
                totalRead += bytesRead;

                if (totalBytes > 0)
                {
                    var percentComplete = (double)totalRead / totalBytes * 100;
                    progress.Report(percentComplete);
                }
            }

            Log.Information("Download complete: {Path} ({Bytes} bytes)", destinationPath, totalRead);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Download failed for {Url}", url);
            throw;
        }
    }
}

public class ModValidationResult
{
    public bool IsValid { get; set; }
    public Mod? Mod { get; set; }
    public bool HasMissingDependencies { get; set; }
    public List<string> MissingDependencies { get; set; } = new();
    public bool HasConflicts { get; set; }
    public List<Mod> ConflictingMods { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}