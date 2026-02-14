using LiteDB;
using Newtonsoft.Json;
using Octokit;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Yellowcake.Helpers;
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
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _installService = installService ?? throw new ArgumentNullException(nameof(installService));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _gh = gh ?? throw new ArgumentNullException(nameof(gh));
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
        var modStoragePath = GetModStoragePath(mod.Id);
        var voiceStoragePath = Path.Combine(PathService.GetModsDirectory(), "WSOYappinator", "audio");

        try
        {
            await DownloadFileWithProgressAsync(downloadUrl, tempFile, progress, ct);

            var fileType = DetectFileType(tempFile);
            
            Log.Information("Detected file type: {FileType} for {ModName}", fileType, mod.Name);

            Directory.CreateDirectory(modStoragePath);

            switch (fileType)
            {
                case FileType.Dll:
                    await InstallRawDllToStorageAsync(mod, tempFile, modStoragePath);
                    break;

                case FileType.Archive:
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

                    if(mod.IsVoicePack) await ExtractToStorageAsync(tempFile, voiceStoragePath, ct);
                    await ExtractToStorageAsync(tempFile, modStoragePath, ct);
                    break;

                case FileType.Invalid:
                default:
                    throw new InvalidOperationException(
                        $"Downloaded file is not a valid archive or DLL. " +
                        $"The download may have failed or the link may be incorrect.");
            }

            mod.MarkAsInstalled(mod.Version, mod.Hash);
            
            await EnableModAsync(mod);

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

    private static FileType DetectFileType(string filePath)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            var buffer = new byte[4];
            fs.Read(buffer, 0, 4);

            if (buffer[0] == 0x50 && buffer[1] == 0x4B)
            {
                Log.Debug("Detected ZIP archive");
                return FileType.Archive;
            }

            if (buffer[0] == 0x52 && buffer[1] == 0x61 && buffer[2] == 0x72 && buffer[3] == 0x21)
            {
                Log.Debug("Detected RAR archive");
                return FileType.Archive;
            }

            if (buffer[0] == 0x37 && buffer[1] == 0x7A && buffer[2] == 0xBC && buffer[3] == 0xAF)
            {
                Log.Debug("Detected 7z archive");
                return FileType.Archive;
            }

            if (buffer[0] == 0x1F && buffer[1] == 0x8B)
            {
                Log.Debug("Detected GZIP archive");
                return FileType.Archive;
            }

            if (buffer[0] == 0x4D && buffer[1] == 0x5A)
            {
                Log.Information("Detected Windows PE file (DLL/EXE)");
                
                fs.Seek(0x3C, SeekOrigin.Begin);
                var peOffset = new byte[4];
                fs.Read(peOffset, 0, 4);
                var peHeaderOffset = BitConverter.ToInt32(peOffset, 0);
                
                if (peHeaderOffset > 0 && peHeaderOffset < fs.Length - 4)
                {
                    fs.Seek(peHeaderOffset, SeekOrigin.Begin);
                    var peSignature = new byte[4];
                    fs.Read(peSignature, 0, 4);
                    
                    if (peSignature[0] == 0x50 && peSignature[1] == 0x45)
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

    private static string GetModStoragePath(string modId)
    {
        return Path.Combine(PathService.GetModsDirectory(), modId);
    }

    private string GetGameTargetPath(Mod mod)
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

        if (mod.IsVoicePack)
        {
            return Path.Combine(gameDir, "BepInEx", "plugins", "WSOYappinator", "audio", mod.Id);
        }

        if (mod.IsMission)
        {
            return Path.Combine(gameDir, "Missions", mod.Id);
        }

        return Path.Combine(gameDir, "BepInEx", "plugins", mod.Id);
    }

    private async Task InstallRawDllToStorageAsync(Mod mod, string dllPath, string storagePath)
    {
        try
        {
            var fileName = $"{mod.Id}.dll";
            var targetPath = Path.Combine(storagePath, fileName);

            Log.Information("Copying raw DLL to storage: {Path}", targetPath);

            await Task.Run(() => File.Copy(dllPath, targetPath, overwrite: true));

            Log.Information("Successfully stored raw DLL: {ModName}", mod.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to store raw DLL for {ModName}", mod.Name);
            throw;
        }
    }

    private async Task ExtractToStorageAsync(string archivePath, string storagePath, CancellationToken ct)
    {
        try
        {
            Log.Information("Extracting archive to storage: {Path}", storagePath);

            await Task.Run(() => _installService.ExtractArchiveToPath(archivePath, storagePath, ct), ct);

            Log.Information("Successfully extracted archive to storage");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to extract archive to storage");
            throw;
        }
    }

    private async Task EnableModAsync(Mod mod)
    {
        var sourcePath = GetModStoragePath(mod.Id);
        var targetPath = GetGameTargetPath(mod);

        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException($"Mod storage not found: {sourcePath}");
        }

        try
        {
            var targetParent = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetParent))
            {
                Directory.CreateDirectory(targetParent);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && JunctionManager.IsSupported)
            {
                if (Directory.Exists(targetPath) || File.Exists(targetPath))
                {
                    if (JunctionManager.IsSymbolicLink(targetPath))
                    {
                        JunctionManager.Remove(targetPath);
                    }
                    else
                    {
                        if (Directory.Exists(targetPath))
                        {
                            Directory.Delete(targetPath, true);
                        }
                        else
                        {
                            File.Delete(targetPath);
                        }
                    }
                }

                await Task.Run(() => JunctionManager.Create(targetPath, sourcePath, overwrite: true));
                Log.Information("Created junction for {ModId}: {Target} -> {Source}", mod.Id, targetPath, sourcePath);
            }
            else
            {
                if (Directory.Exists(targetPath))
                {
                    Directory.Delete(targetPath, true);
                }

                await Task.Run(() => CopyDirectory(sourcePath, targetPath));
                Log.Information("Copied mod files for {ModId} to {Target}", mod.Id, targetPath);
            }

            mod.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to enable mod: {ModId}", mod.Id);
            throw;
        }
    }

    private async Task DisableModAsync(Mod mod)
    {
        var targetPath = GetGameTargetPath(mod);

        try
        {
            if (!Directory.Exists(targetPath) && !File.Exists(targetPath))
            {
                Log.Warning("Target path does not exist for mod: {ModId}", mod.Id);
                return;
            }

            if (JunctionManager.IsSymbolicLink(targetPath))
            {
                await Task.Run(() => JunctionManager.Remove(targetPath));
                Log.Information("Removed junction for mod: {ModId}", mod.Id);
            }
            else
            {
                await Task.Run(() =>
                {
                    if (Directory.Exists(targetPath))
                    {
                        Directory.Delete(targetPath, true);
                    }
                    else if (File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                    }
                });
                Log.Information("Deleted mod files for: {ModId}", mod.Id);
            }

            mod.IsEnabled = false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to disable mod: {ModId}", mod.Id);
            throw;
        }
    }

    public async Task ToggleModAsync(Mod mod)
    {
        if (mod == null) throw new ArgumentNullException(nameof(mod));

        try
        {
            if (mod.IsEnabled)
            {
                await DisableModAsync(mod);
                NotificationService.Instance.Info($"{mod.Name} is now disabled");
            }
            else
            {
                await EnableModAsync(mod);
                NotificationService.Instance.Success($"{mod.Name} is now enabled");
            }

            _db.Upsert("addons", mod);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to toggle mod: {ModId}", mod.Id);
            NotificationService.Instance.Error($"Failed to toggle {mod.Name}");
            throw;
        }
    }

    public void ToggleMod(string modId, bool isEnabled)
    {
        if (string.IsNullOrWhiteSpace(modId))
        {
            throw new ArgumentNullException(nameof(modId));
        }

        var mod = _db.GetAll<Mod>("addons").FirstOrDefault(m => m.Id == modId);
        if (mod == null)
        {
            throw new InvalidOperationException($"Mod not found: {modId}");
        }

        if (mod.IsEnabled == isEnabled)
        {
            return;
        }

        Task.Run(async () => await ToggleModAsync(mod)).Wait();
    }

    public async Task DeleteModAsync(Mod mod)
    {
        if (mod == null) throw new ArgumentNullException(nameof(mod));

        try
        {
            await DisableModAsync(mod);

            var storagePath = GetModStoragePath(mod.Id);

            if (Directory.Exists(storagePath))
            {
                await Task.Run(() => Directory.Delete(storagePath, true));
                Log.Information("Deleted mod storage: {Path}", storagePath);
            }

            _db.Delete("addons", mod.Id);

            Log.Information("Successfully deleted mod: {ModId}", mod.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete mod: {ModId}", mod.Id);
            throw;
        }
    }

    public void DeleteMod(string modId)
    {
        if (string.IsNullOrWhiteSpace(modId))
        {
            throw new ArgumentNullException(nameof(modId));
        }

        var mod = _db.GetAll<Mod>("addons").FirstOrDefault(m => m.Id == modId);
        if (mod == null)
        {
            throw new InvalidOperationException($"Mod not found: {modId}");
        }

        Task.Run(async () => await DeleteModAsync(mod)).Wait();
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

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        var dir = new DirectoryInfo(sourceDir);

        if (!dir.Exists)
        {
            throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");
        }

        Directory.CreateDirectory(destinationDir);

        foreach (var file in dir.GetFiles())
        {
            var targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath, true);
        }

        foreach (var subDir in dir.GetDirectories())
        {
            var newDestinationDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectory(subDir.FullName, newDestinationDir);
        }
    }

    public bool VerifyModStorage(string modId)
    {
        try
        {
            var storagePath = GetModStoragePath(modId);
            
            if (!Directory.Exists(storagePath))
            {
                Log.Warning("Mod storage not found: {Path}", storagePath);
                return false;
            }

            var hasFiles = Directory.EnumerateFileSystemEntries(storagePath, "*", SearchOption.AllDirectories).Any();
            
            if (!hasFiles)
            {
                Log.Warning("Mod storage is empty: {Path}", storagePath);
                return false;
            }

            Log.Debug("Mod storage verified for {ModId}", modId);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Verification failed for {ModId}", modId);
            return false;
        }
    }

    public async Task RepairModAsync(Mod mod)
    {
        if (mod == null) throw new ArgumentNullException(nameof(mod));

        try
        {
            Log.Information("Attempting to repair mod: {ModId}", mod.Id);

            if (!VerifyModStorage(mod.Id))
            {
                throw new InvalidOperationException("Mod storage is missing or corrupted. Please reinstall the mod.");
            }

            if (mod.IsEnabled)
            {
                await DisableModAsync(mod);
                await EnableModAsync(mod);
                Log.Information("Successfully repaired mod: {ModId}", mod.Id);
                NotificationService.Instance.Success($"Repaired {mod.Name}");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to repair mod: {ModId}", mod.Id);
            NotificationService.Instance.Error($"Failed to repair {mod.Name}");
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