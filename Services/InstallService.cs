using Serilog;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Yellowcake.Models;

namespace Yellowcake.Services;

public class InstallService
{
    public string ModsPath { get; }
    private readonly PathService _pathService;

    public InstallService(string modsPath, PathService pathService)
    {
        ModsPath = modsPath ?? throw new ArgumentNullException(nameof(modsPath));
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));

        if (!Directory.Exists(ModsPath))
        {
            Directory.CreateDirectory(ModsPath);
            Log.Information("[InstallService] Created mods directory: {Path}", ModsPath);
        }
    }

    public async Task InstallModAsync(Mod mod, string archivePath, CancellationToken ct = default)
    {
        if (mod == null) throw new ArgumentNullException(nameof(mod));
        if (string.IsNullOrWhiteSpace(archivePath)) throw new ArgumentNullException(nameof(archivePath));
        if (!File.Exists(archivePath)) throw new FileNotFoundException($"Archive not found: {archivePath}");

        Log.Information("[InstallService] Installing {ModName} from {Archive}", mod.Name, archivePath);

        var installPath = DetermineInstallPath(mod);

        if (!Directory.Exists(installPath))
        {
            Directory.CreateDirectory(installPath);
        }

        try
        {
            await Task.Run(() => ExtractArchive(archivePath, installPath, ct), ct);

            Log.Information("[InstallService] Successfully installed {ModName} to {Path}", mod.Name, installPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[InstallService] Installation failed for {ModName}", mod.Name);
            
            try
            {
                if (Directory.Exists(installPath))
                {
                    Directory.Delete(installPath, true);
                }
            }
            catch (Exception cleanupEx)
            {
                Log.Warning(cleanupEx, "[InstallService] Failed to cleanup after failed installation");
            }

            throw;
        }
    }
    public async Task ExtractWithSmartRoot(Stream archiveStream, string targetPath)
    {
        if (archiveStream == null) throw new ArgumentNullException(nameof(archiveStream));
        if (string.IsNullOrWhiteSpace(targetPath)) throw new ArgumentNullException(nameof(targetPath));

        await Task.Run(() =>
        {
            using var zipArchive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false);
            
            var entries = zipArchive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
            if (!entries.Any()) return;

            var firstEntry = entries.First().FullName;
            var rootCandidate = firstEntry.Contains('/') || firstEntry.Contains('\\')
                ? firstEntry.Split(new[] { '/', '\\' }, 2)[0] + "/"
                : null;

            bool hasCommonRoot = rootCandidate != null && 
                entries.All(e => e.FullName.StartsWith(rootCandidate, StringComparison.OrdinalIgnoreCase));

            Directory.CreateDirectory(targetPath);

            foreach (var entry in entries)
            {
                var relativePath = hasCommonRoot 
                    ? entry.FullName.Substring(rootCandidate!.Length)
                    : entry.FullName;

                var destPath = Path.Combine(targetPath, relativePath);
                var destDir = Path.GetDirectoryName(destPath);

                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                entry.ExtractToFile(destPath, overwrite: true);
            }

            Log.Debug("[InstallService] Extracted {Count} files to {Path} (smart root: {HasRoot})", 
                entries.Count, targetPath, hasCommonRoot);
        });
    }

    public async Task InstallCategorizedMod(Mod mod, Stream archiveStream, string gamePath)
    {
        if (mod == null) throw new ArgumentNullException(nameof(mod));
        if (archiveStream == null) throw new ArgumentNullException(nameof(archiveStream));

        var finalDir = Path.Combine(ModsPath, mod.Id);
        string? backupDir = null;

        try
        {
            if (Directory.Exists(finalDir))
            {
                backupDir = finalDir + ".backup_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                Log.Debug("[InstallService] Backing up existing installation to {Backup}", backupDir);
                Directory.Move(finalDir, backupDir);
            }

            await ExtractWithSmartRoot(archiveStream, finalDir);

            if (backupDir != null && Directory.Exists(backupDir))
            {
                Directory.Delete(backupDir, true);
                Log.Debug("[InstallService] Removed backup directory");
            }

            Log.Information("[InstallService] Installed categorized mod {ModName} to {Path}", mod.Name, finalDir);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[InstallService] Failed to install categorized mod {ModName}", mod.Name);

            if (backupDir != null && Directory.Exists(backupDir))
            {
                try
                {
                    if (Directory.Exists(finalDir))
                    {
                        Directory.Delete(finalDir, true);
                    }
                    Directory.Move(backupDir, finalDir);
                    Log.Information("[InstallService] Restored backup after failed installation");
                }
                catch (Exception restoreEx)
                {
                    Log.Error(restoreEx, "[InstallService] Failed to restore backup");
                }
            }

            throw;
        }
    }

    public async Task InstallRawDll(Mod mod, Stream dllStream, string gamePath)
    {
        if (mod == null) throw new ArgumentNullException(nameof(mod));
        if (dllStream == null) throw new ArgumentNullException(nameof(dllStream));

        var modDir = Path.Combine(ModsPath, mod.Id);
        Directory.CreateDirectory(modDir);

        var fileName = SanitizeFileName(mod.DownloadUrl) ?? $"{mod.Id}.dll";
        if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".dll";
        }

        var dllPath = Path.Combine(modDir, fileName);

        await Task.Run(async () =>
        {
            using var fileStream = new FileStream(dllPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await dllStream.CopyToAsync(fileStream);
        });

        Log.Information("[InstallService] Installed raw DLL {FileName} to {Path}", fileName, modDir);
    }

    private string? SanitizeFileName(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        try
        {
            var uri = new Uri(url);
            var segments = uri.Segments;
            var lastSegment = segments.LastOrDefault()?.Trim('/');

            if (string.IsNullOrWhiteSpace(lastSegment)) return null;

            var fileName = lastSegment.Split('?')[0];

            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (var c in invalidChars)
            {
                fileName = fileName.Replace(c.ToString(), "_");
            }

            return fileName;
        }
        catch
        {
            return null;
        }
    }

    private string DetermineInstallPath(Mod mod)
    {
        var gamePath = _pathService.GamePath;
        if (string.IsNullOrEmpty(gamePath))
        {
            throw new InvalidOperationException("Game path not set");
        }

        var gameDir = Path.GetDirectoryName(gamePath);
        if (string.IsNullOrEmpty(gameDir))
        {
            throw new InvalidOperationException("Invalid game path");
        }

        // Voice packs go to WSOYappinator/audio
        if (mod.IsVoicePack)
        {
            var voicePackDir = Path.Combine(gameDir, "BepInEx", "plugins", "WSOYappinator", "audio", mod.Id);
            Log.Debug("[InstallService] Voice pack will install to: {Path}", voicePackDir);
            return voicePackDir;
        }

        // Missions go to Missions folder
        if (mod.IsMission)
        {
            var missionsDir = Path.Combine(gameDir, "Missions", mod.Id);
            Log.Debug("[InstallService] Mission will install to: {Path}", missionsDir);
            return missionsDir;
        }

        // Liveries and regular plugins go to BepInEx/plugins
        var pluginsDir = Path.Combine(gameDir, "BepInEx", "plugins", mod.Id);
        Log.Debug("[InstallService] Plugin will install to: {Path}", pluginsDir);
        return pluginsDir;
    }

    private void ExtractArchive(string archivePath, string targetPath, CancellationToken ct)
    {
        try
        {
            Log.Debug("[InstallService] Extracting {Archive} to {Target}", archivePath, targetPath);

            using var archive = ArchiveFactory.Open(archivePath);
            
            var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
            Log.Debug("[InstallService] Found {Count} files to extract", entries.Count);

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                var destinationPath = Path.Combine(targetPath, entry.Key);
                var destinationDir = Path.GetDirectoryName(destinationPath);

                if (!string.IsNullOrEmpty(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }

                entry.WriteToFile(destinationPath, new ExtractionOptions
                {
                    ExtractFullPath = true,
                    Overwrite = true
                });
            }

            Log.Information("[InstallService] Extracted {Count} files to {Path}", entries.Count, targetPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[InstallService] Failed to extract archive: {Archive}", archivePath);
            
            if (File.Exists(archivePath))
            {
                var fileInfo = new FileInfo(archivePath);
                Log.Error("[InstallService] Archive size: {Size} bytes", fileInfo.Length);
                
                using var fs = File.OpenRead(archivePath);
                var buffer = new byte[100];
                var read = fs.Read(buffer, 0, 100);
                var preview = System.Text.Encoding.ASCII.GetString(buffer, 0, Math.Min(read, 100));
                Log.Error("[InstallService] File preview (first 100 bytes): {Preview}", preview);
            }
            
            throw new InvalidOperationException(
                "Failed to extract archive. The file may be corrupted or not a valid ZIP file. " +
                "If this is a Google Drive link, the download may have failed.", ex);
        }
    }

    public bool VerifyInstallation(Mod mod)
    {
        try
        {
            var installPath = DetermineInstallPath(mod);
            
            if (!Directory.Exists(installPath))
            {
                Log.Warning("[InstallService] Installation directory not found: {Path}", installPath);
                return false;
            }

            var hasFiles = Directory.EnumerateFileSystemEntries(installPath, "*", SearchOption.AllDirectories).Any();
            
            if (!hasFiles)
            {
                Log.Warning("[InstallService] Installation directory is empty: {Path}", installPath);
                return false;
            }

            Log.Debug("[InstallService] Installation verified for {ModId}", mod.Id);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[InstallService] Verification failed for {ModId}", mod.Id);
            return false;
        }
    }

    public string? GetInstallPath(string modId)
    {
        var gamePath = _pathService.GamePath;
        if (string.IsNullOrEmpty(gamePath)) return null;

        var gameDir = Path.GetDirectoryName(gamePath);
        if (string.IsNullOrEmpty(gameDir)) return null;

        // Check all possible locations
        var possiblePaths = new[]
        {
            Path.Combine(gameDir, "BepInEx", "plugins", modId),
            Path.Combine(gameDir, "BepInEx", "plugins", "WSOYappinator", "audio", modId),
            Path.Combine(gameDir, "Missions", modId)
        };

        return possiblePaths.FirstOrDefault(Directory.Exists);
    }
}