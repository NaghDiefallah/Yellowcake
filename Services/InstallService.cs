using Serilog;
using SharpCompress.Archives;
using SharpCompress.Common;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Yellowcake.Models;

namespace Yellowcake.Services;

public class InstallService
{
    public string ModsPath { get; }
    private readonly PathService _pathService;

    public InstallService(PathService pathService)
    {
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        ModsPath = PathService.GetModsDirectory();

        if (!Directory.Exists(ModsPath))
        {
            Directory.CreateDirectory(ModsPath);
            Log.Information("[InstallService] Created mods directory: {Path}", ModsPath);
        }
    }

    public void ExtractArchiveToPath(string archivePath, string targetPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
            throw new ArgumentNullException(nameof(archivePath));
        
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentNullException(nameof(targetPath));

        if (!File.Exists(archivePath))
            throw new FileNotFoundException($"Archive not found: {archivePath}");

        try
        {
            Log.Debug("[InstallService] Extracting {Archive} to {Target}", archivePath, targetPath);

            if (!Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(targetPath);
            }

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
            }
            
            throw new InvalidOperationException(
                "Failed to extract archive. The file may be corrupted or not a valid ZIP file.", ex);
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
            await Task.Run(() => ExtractArchiveToPath(archivePath, installPath, ct), ct);

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

        if (mod.IsVoicePack)
        {
            var voicePackDir = Path.Combine(gameDir, "BepInEx", "plugins", "WSOYappinator", "audio", mod.Id);
            Log.Debug("[InstallService] Voice pack will install to: {Path}", voicePackDir);
            return voicePackDir;
        }

        if (mod.IsMission)
        {
            var missionsDir = Path.Combine(gameDir, "Missions", mod.Id);
            Log.Debug("[InstallService] Mission will install to: {Path}", missionsDir);
            return missionsDir;
        }

        var pluginsDir = Path.Combine(gameDir, "BepInEx", "plugins", mod.Id);
        Log.Debug("[InstallService] Plugin will install to: {Path}", pluginsDir);
        return pluginsDir;
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

        var possiblePaths = new[]
        {
            Path.Combine(gameDir, "BepInEx", "plugins", modId),
            Path.Combine(gameDir, "BepInEx", "plugins", "WSOYappinator", "audio", modId),
            Path.Combine(gameDir, "Missions", modId)
        };

        return possiblePaths.FirstOrDefault(Directory.Exists);
    }
}