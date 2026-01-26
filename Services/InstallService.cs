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
    public string ModsPath => _modsPath;

    public InstallService(string modsPath)
    {
        _modsPath = modsPath;
        if (!Directory.Exists(_modsPath))
        {
            Directory.CreateDirectory(_modsPath);
        }
    }

    public async Task InstallCategorizedMod(Mod mod, MemoryStream ms, string gamePath)
    {
        string gameDir = GetGameDir(gamePath);

        if (IsRawDll(ms))
        {
            await InstallRawDll(ms, mod, gamePath);
            return;
        }

        bool isBasePlugin = mod.Id.Contains("WSOYappinator", StringComparison.OrdinalIgnoreCase);
        if (isBasePlugin)
        {
            await ExtractMod(ms, mod.Id);
            LinkMod(mod.Id, gamePath, true);
            return;
        }

        switch (mod.Category)
        {
            case "Audio":
            case "Voice Pack":
                await InstallVoicePack(ms, gameDir, mod);
                break;

            case "Livery":
            case "Visual":
                string liveryTarget = Path.Combine(gameDir, "NuclearOption_Data", "StreamingAssets", "Liveries", mod.Id);
                await ExtractToDirectory(ms, liveryTarget);
                break;

            default:
                await ExtractMod(ms, mod.Id);
                LinkMod(mod.Id, gamePath, true);
                break;
        }
    }

    private bool IsRawDll(MemoryStream ms)
    {
        if (ms.Length < 2) return false;
        long pos = ms.Position;
        ms.Position = 0;
        int b1 = ms.ReadByte();
        int b2 = ms.ReadByte();
        ms.Position = pos;
        return b1 == 0x4D && b2 == 0x5A;
    }

    private async Task InstallRawDll(MemoryStream ms, Mod mod, string gamePath)
    {
        string targetDir = Path.Combine(_modsPath, mod.Id);
        Directory.CreateDirectory(targetDir);
        string dllPath = Path.Combine(targetDir, $"{mod.Id}.dll");
        await File.WriteAllBytesAsync(dllPath, ms.ToArray());
        LinkMod(mod.Id, gamePath, true);
    }

    public async Task ExtractToDirectory(MemoryStream ms, string targetPath)
    {
        if (ms == null || ms.Length == 0) throw new ArgumentException("Source stream is empty.");

        await Task.Run(() =>
        {
            try
            {
                ms.Position = 0;
                if (!ArchiveFactory.IsArchive(ms, out _))
                {
                    throw new InvalidOperationException("The file is not a valid archive (Zip, Rar, 7z).");
                }

                if (Directory.Exists(targetPath)) Directory.Delete(targetPath, true);
                Directory.CreateDirectory(targetPath);

                ms.Position = 0;
                using var archive = ArchiveFactory.Open(ms);
                string absoluteTarget = Path.GetFullPath(targetPath);
                string rootPrefix = GetRootPrefix(archive);

                foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                {
                    string entryKey = entry.Key.Replace('\\', '/');
                    if (!entryKey.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                    string relativePath = entryKey[rootPrefix.Length..].TrimStart('/');
                    if (string.IsNullOrWhiteSpace(relativePath)) continue;

                    string destination = Path.GetFullPath(Path.Combine(absoluteTarget, relativePath));

                    if (!destination.StartsWith(absoluteTarget, StringComparison.OrdinalIgnoreCase)) continue;

                    string? dir = Path.GetDirectoryName(destination);
                    if (dir != null) Directory.CreateDirectory(dir);

                    using var fs = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
                    entry.WriteTo(fs);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Extraction failed for {Path}", targetPath);
                NotificationService.Instance.Error($"Extraction failed: {Path.GetFileName(targetPath)}");
                throw;
            }
        });
    }

    public async Task ExtractMod(MemoryStream ms, string modId) =>
        await ExtractToDirectory(ms, Path.Combine(_modsPath, modId));

    public void LinkMod(string modId, string gamePath, bool enable)
    {
        string gameDir = GetGameDir(gamePath);
        string pluginsDir = Path.Combine(gameDir, "BepInEx", "plugins");
        string sourcePath = Path.Combine(_modsPath, modId);
        string junctionTarget = Path.Combine(pluginsDir, modId);

        try
        {
            if (enable)
            {
                if (!Directory.Exists(sourcePath)) return;
                if (!Directory.Exists(pluginsDir)) Directory.CreateDirectory(pluginsDir);
                if (Directory.Exists(junctionTarget)) Directory.Delete(junctionTarget, true);

                JunctionManager.Create(junctionTarget, sourcePath, true);
                Log.Debug("Linked {Id}", modId);
            }
            else if (Directory.Exists(junctionTarget))
            {
                Directory.Delete(junctionTarget, true);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Link operation failed for {Id}", modId);
        }
    }

    private async Task InstallVoicePack(MemoryStream ms, string gameDir, Mod mod)
    {
        string yappinatorPath = Path.Combine(gameDir, "BepInEx", "plugins", "WSOYappinator");

        if (!Directory.Exists(yappinatorPath))
            yappinatorPath = Path.Combine(gameDir, "Mods", "WSOYappinator");

        if (!Directory.Exists(yappinatorPath))
            throw new DirectoryNotFoundException("WSOYappinator not found. Please install the WSOYappinator plugin first.");

        string target = Path.Combine(yappinatorPath, "audio", mod.Id);
        ms.Position = 0;
        await ExtractToDirectory(ms, target);
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
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                entry.ExtractToFile(destPath, true);
            }
        });
    }

    public void CleanupMods(string id)
    {
        string path = Path.Combine(_modsPath, id);
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }

    private string GetGameDir(string path) =>
        File.Exists(path) ? Path.GetDirectoryName(path) ?? path : path;

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
        int firstSlash = firstKey.IndexOf('/');
        if (firstSlash != -1)
        {
            string root = firstKey[..(firstSlash + 1)];
            if (entries.All(e => e.Key.Replace('\\', '/').StartsWith(root, StringComparison.OrdinalIgnoreCase)))
                return root;
        }

        return string.Empty;
    }
}