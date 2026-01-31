using Serilog;
using SharpCompress.Archives;
using SharpCompress.Common;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Yellowcake.Helpers;
using Yellowcake.Models;

namespace Yellowcake.Services;

public class InstallService
{
    private readonly string _modsPath;
    private readonly PathService _pathService;

    public string ModsPath => _modsPath;

    public InstallService(string modsPath, PathService pathService)
    {
        _modsPath = modsPath ?? throw new ArgumentNullException(nameof(modsPath));
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));

        if (!Directory.Exists(_modsPath))
            Directory.CreateDirectory(_modsPath);
    }

    public async Task InstallCategorizedMod(Mod mod, MemoryStream ms, string gamePath)
    {
        if (mod == null || ms == null) return;

        string gameDir = GetGameDir(gamePath);
        ms.Position = 0;

        if (IsRawDll(ms))
        {
            Log.Information("Installing raw DLL for {ModName}", mod.Name);
            await InstallRawDll(ms, mod, gamePath);
            return;
        }

        string targetDir = mod.IsMission ? Path.Combine(_pathService.GetMissionsDirectory() ?? _modsPath, mod.Id)
                         : mod.IsVoicePack ? Path.Combine(_pathService.GetVoicePacksDirectory() ?? _modsPath, mod.Id)
                         : mod.IsLivery ? Path.Combine(gameDir, "BepInEx", "plugins", "LiveryPlus", mod.Id)
                         : Path.Combine(_modsPath, mod.Id);

        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        bool isCorePlugin = mod.Id.Contains("WSOYappinator", StringComparison.OrdinalIgnoreCase) ||
                            mod.Id.Contains("LiveryPlus", StringComparison.OrdinalIgnoreCase);

        Log.Information("Extracting {ModName} to {TargetDir} (Core: {IsCore})", mod.Name, targetDir, isCorePlugin);

        if (isCorePlugin)
        {
            await ExtractFlattened(ms, targetDir);
        }
        else
        {
            await ExtractToDirectory(ms, targetDir);
        }

        bool needsLinking = !mod.IsMission && !mod.IsVoicePack && !mod.IsLivery;
        if (needsLinking)
        {
            Log.Debug("Linking standard mod: {ModId}", mod.Id);
            LinkMod(mod.Id, gamePath, true);
        }

        Log.Information("Installation successful: {ModName}", mod.Name);
    }

    private bool IsRawDll(MemoryStream ms)
    {
        if (ms.Length < 2) return false;
        var buffer = new byte[2];
        ms.Position = 0;
        ms.ReadExactly(buffer, 0, 2);
        ms.Position = 0;
        return buffer[0] == 0x4D && buffer[1] == 0x5A;
    }

    private async Task InstallRawDll(MemoryStream ms, Mod mod, string gamePath)
    {
        string targetDir = Path.Combine(_modsPath, mod.Id);
        Directory.CreateDirectory(targetDir);

        string dllPath = Path.Combine(targetDir, $"{mod.Id}.dll");
        await File.WriteAllBytesAsync(dllPath, ms.ToArray());

        LinkMod(mod.Id, gamePath, true);
    }

    public async Task ExtractFlattened(MemoryStream ms, string targetPath)
    {
        await Task.Run(() =>
        {
            using var archive = ArchiveFactory.Open(ms);
            Directory.CreateDirectory(targetPath);

            foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
            {
                string fileName = Path.GetFileName(entry.Key);
                entry.WriteToFile(Path.Combine(targetPath, fileName), new ExtractionOptions { Overwrite = true });
            }
        });
    }

    public async Task ExtractToDirectory(MemoryStream ms, string targetPath)
    {
        if (ms == null || ms.Length == 0) throw new ArgumentException("Source stream is empty.");

        await Task.Run(() =>
        {
            try
            {
                using var archive = ArchiveFactory.Open(ms);
                string absoluteTarget = Path.GetFullPath(targetPath);
                string rootPrefix = GetRootPrefix(archive);

                foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                {
                    string normalizedKey = entry.Key.Replace('\\', '/');
                    string relativePath = normalizedKey.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
                        ? normalizedKey[rootPrefix.Length..].TrimStart('/')
                        : normalizedKey;

                    if (string.IsNullOrWhiteSpace(relativePath)) continue;

                    string destination = Path.GetFullPath(Path.Combine(absoluteTarget, relativePath));
                    if (!destination.StartsWith(absoluteTarget, StringComparison.OrdinalIgnoreCase)) continue;

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.WriteToFile(destination, new ExtractionOptions { Overwrite = true });
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Extraction failed for {Path}", targetPath);
                throw;
            }
        });
    }

    public void LinkMod(string modId, string gamePath, bool enable)
    {
        if (string.IsNullOrWhiteSpace(modId)) return;

        string gameDir = GetGameDir(gamePath);
        string pluginsDir = Path.Combine(gameDir, "BepInEx", "plugins");
        string sourcePath = Path.Combine(_modsPath, modId);
        string linkTarget = Path.Combine(pluginsDir, modId);

        try
        {
            if (Path.Exists(linkTarget))
            {
                Log.Debug("Removing existing entry at {Path}", linkTarget);
                var attributes = File.GetAttributes(linkTarget);

                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    Directory.Delete(linkTarget, true);
                }
                else
                {
                    File.Delete(linkTarget);
                }
            }

            if (enable)
            {
                if (!Directory.Exists(sourcePath))
                {
                    Log.Warning("Link source missing: {Path}", sourcePath);
                    return;
                }

                if (!Directory.Exists(pluginsDir))
                {
                    Directory.CreateDirectory(pluginsDir);
                }

                Log.Information("Linking {ModId}: {Source} -> {Target}", modId, sourcePath, linkTarget);
                Directory.CreateSymbolicLink(linkTarget, sourcePath);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Error(ex, "Insufficient permissions to create symbolic link for {Id}", modId);
            NotificationService.Instance.Error("Permission Denied: Run as Admin or enable Developer Mode.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to manage link for {Id}", modId);
            NotificationService.Instance.Error($"Link failure: {modId}");
        }
    }

    public async Task ExtractBepInEx(MemoryStream ms, string targetDir)
    {
        await Task.Run(() =>
        {
            ms.Position = 0;
            using var archive = new ZipArchive(ms);
            string absoluteTarget = Path.GetFullPath(targetDir);

            foreach (var entry in archive.Entries)
            {
                string destPath = Path.GetFullPath(Path.Combine(absoluteTarget, entry.FullName));
                if (!destPath.StartsWith(absoluteTarget, StringComparison.OrdinalIgnoreCase)) continue;

                if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                {
                    Directory.CreateDirectory(destPath);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    entry.ExtractToFile(destPath, true);
                }
            }
        });
    }

    public void CleanupMods(string id)
    {
        string path = Path.Combine(_modsPath, id);
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }

    private string GetGameDir(string path) =>
        Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path;

    private static string GetRootPrefix(IArchive archive)
    {
        var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
        if (!entries.Any()) return string.Empty;

        var bepEntry = entries.FirstOrDefault(e => e.Key.Contains("BepInEx", StringComparison.OrdinalIgnoreCase));
        if (bepEntry != null)
        {
            int idx = bepEntry.Key.IndexOf("BepInEx", StringComparison.OrdinalIgnoreCase);
            return bepEntry.Key[..idx].Replace('\\', '/');
        }

        string firstKey = entries[0].Key.Replace('\\', '/');
        int slashIdx = firstKey.IndexOf('/');
        if (slashIdx != -1)
        {
            string root = firstKey[..(slashIdx + 1)];
            if (entries.All(e => e.Key.Replace('\\', '/').StartsWith(root, StringComparison.OrdinalIgnoreCase)))
                return root;
        }

        return string.Empty;
    }
}