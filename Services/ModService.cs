using Octokit;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Yellowcake.Models;

namespace Yellowcake.Services;

public class ModService
{
    private readonly DatabaseService _db;
    private readonly InstallService _installer;
    private readonly PathService _pathService;
    private readonly ManifestService _manifests;
    private readonly DownloadService _downloader;

    private const string BepInExUrl = "https://github.com/BepInEx/BepInEx/releases/download/v5.4.21/BepInEx_x64_5.4.21.0.zip";

    public ModService(DatabaseService db, InstallService installer, HttpClient client, GitHubClient github)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _pathService = new PathService(db);
        _manifests = new ManifestService(client);
        _downloader = new DownloadService(client, github);

        Log.Information("Mod service initialized.");
    }

    public string? LoadGamePath() => _db.GetSetting("GamePath");

    public void SaveGamePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        _db.SaveSetting("GamePath", path);
        _pathService.SetGamePath(path);
    }

    public List<Mod> GetInstalledMods() => _db.GetAll<Mod>("addons");

    public bool IsBepInExInstalled()
    {
        var gameDir = GetGameDirectory();
        return !string.IsNullOrWhiteSpace(gameDir) && Directory.Exists(Path.Combine(gameDir, "BepInEx"));
    }

    public async Task InstallBepInExAsync(string targetDir, IProgress<double> progress)
    {
        if (!Directory.Exists(targetDir))
            throw new DirectoryNotFoundException($"Target directory missing: {targetDir}");

        using var stream = await _downloader.DownloadWithProgress(BepInExUrl, null, progress);
        await _installer.ExtractBepInEx(stream, targetDir);
    }

    public Task<List<Mod>> FetchRemoteManifest() => _manifests.FetchRemoteManifest();

    public async Task DownloadAndInstallMod(Mod mod, IProgress<double>? progress, List<Mod> manifest, CancellationToken ct = default, string? effectiveHash = null)
    {
        if (mod == null) return;

        Log.Information("Initializing installation for: {ModName}", mod.Name);

        try
        {
            Log.Debug("Resolving dependencies for {ModName}...", mod.Name);
            await InstallDependencies(mod, manifest, ct);

            var (url, tag) = await ResolveDownloadInfo(mod);
            if (string.IsNullOrWhiteSpace(url))
            {
                Log.Error("Failed to install {ModName}: No valid download URL resolved.", mod.Name);
                return;
            }

            string? hashToVerify = effectiveHash ?? mod.ExpectedHash;

            Log.Information("Downloading {ModName} from {Url}", mod.Name, url);
            using var stream = await _downloader.DownloadWithProgress(url, hashToVerify, progress, ct);

            Log.Information("Extracting and processing files for {ModName}...", mod.Name);
            await ProcessModFiles(stream, mod, tag);

            Log.Information("Installation completed successfully: {ModName}", mod.Name);
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Installation task aborted by user or system: {ModName}", mod.Name);
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Critical failure during installation of {ModName}", mod.Name);
            throw;
        }
    }

    private async Task<(string? url, string tag)> ResolveDownloadInfo(Mod mod)
    {
        string currentVersion = mod.Version ?? "1.0.0";

        if (!string.IsNullOrEmpty(mod.DownloadUrl))
            return (mod.DownloadUrl, currentVersion);

        if (string.IsNullOrWhiteSpace(mod.GitHubUrl))
            return (null, currentVersion);

        string url = mod.GitHubUrl.TrimEnd('/');

        if (url.EndsWith(".zip") || url.EndsWith(".7z") || url.EndsWith(".dll") || url.Contains("/raw/"))
            return (url, currentVersion);

        try
        {
            var uri = new Uri(url);
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length >= 2)
            {
                var owner = segments[0];
                var repo = segments[1].Replace(".git", "", StringComparison.OrdinalIgnoreCase);
                return await _downloader.GetLatestReleaseInfo(owner, repo);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "GitHub lookup failed for {ModName}", mod.Name);
        }

        return (url, currentVersion);
    }

    private async Task ProcessModFiles(MemoryStream stream, Mod mod, string tag)
    {
        if (stream == null || stream.Length == 0)
        {
            Log.Error("ProcessModFiles failed: Provided stream is empty or null for {ModName}", mod?.Name);
            return;
        }

        var gameDir = GetGameDirectory();
        if (string.IsNullOrWhiteSpace(mod.Id)) mod.Id = Guid.NewGuid().ToString();

        stream.Position = 0;

        await Task.Run(async () =>
        {
            try
            {
                Log.Information("Starting extraction and categorization for {ModName} (Version: {Tag})", mod.Name, tag);

                await _installer.InstallCategorizedMod(mod, stream, gameDir);

                if (ShouldLinkMod(mod))
                {
                    Log.Debug("Linking mod files for {ModName}...", mod.Name);
                    _installer.LinkMod(mod.Id, gameDir, true);
                }

                mod.Version = tag;
                mod.LatestVersion = tag;
                mod.IsEnabled = true;
                mod.IsInstalled = true;
                mod.HasUpdate = false;

                _db.Upsert("addons", mod);

                Log.Information("Successfully processed and registered {ModName}", mod.Name);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Physical file processing failed for {ModName}", mod.Name);
                throw;
            }
        });
    }

    public void ToggleMod(string id, bool enable)
    {
        var gameDir = GetGameDirectory();
        var mod = GetInstalledMods().FirstOrDefault(m => m.Id == id);

        if (mod == null || string.IsNullOrEmpty(gameDir)) return;

        if (ShouldLinkMod(mod))
        {
            _installer.LinkMod(id, gameDir, enable);
        }

        mod.IsEnabled = enable;
        _db.Upsert("addons", mod);
    }

    public void DeleteMod(string id)
    {
        ToggleMod(id, false);
        _installer.CleanupMods(id);
        _db.Delete("addons", id);
    }

    public bool IsUpdateAvailable(Mod local, Mod remote)
    {
        if (string.IsNullOrWhiteSpace(local.Version) || string.IsNullOrWhiteSpace(remote.Version))
            return false;

        var vLocal = CleanVersion(local.Version);
        var vRemote = CleanVersion(remote.Version);

        if (Version.TryParse(vLocal, out var verL) && Version.TryParse(vRemote, out var verR))
            return verR > verL;

        return !string.Equals(vLocal, vRemote, StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanVersion(string v)
    {
        var match = Regex.Match(v, @"(\d+\.)*(\d+)");
        return match.Success ? match.Value : "0.0.0";
    }

    private bool ShouldLinkMod(Mod mod)
    {
        return !mod.IsVoicePack &&
               !mod.IsLivery &&
               !string.Equals(mod.Category, "Data", StringComparison.OrdinalIgnoreCase);
    }

    private async Task InstallDependencies(Mod mod, List<Mod> manifest, CancellationToken ct)
    {
        if (mod.Dependencies == null || !mod.Dependencies.Any()) return;

        var installed = GetInstalledMods().Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var depId in mod.Dependencies)
        {
            if (ct.IsCancellationRequested || installed.Contains(depId)) continue;

            var dependency = manifest.FirstOrDefault(m => string.Equals(m.Id, depId, StringComparison.OrdinalIgnoreCase));
            if (dependency != null)
            {
                await DownloadAndInstallMod(dependency, null, manifest, ct);
            }
        }
    }

    public string GetGameDirectory()
    {
        var path = _pathService.GamePath ?? LoadGamePath();
        if (string.IsNullOrEmpty(path)) return string.Empty;

        return File.Exists(path) ? Path.GetDirectoryName(path) ?? string.Empty : path;
    }

    public void CreateDesktopShortcut()
    {
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string shortcutPath = Path.Combine(desktop, "Nuclear Option (Modded).lnk");
            string? exePath = _pathService.GamePath ?? LoadGamePath();

            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                NotificationService.Instance.Error("Game executable not found.");
                return;
            }

            Type t = Type.GetTypeFromProgID("WScript.Shell")!;
            dynamic shell = Activator.CreateInstance(t)!;
            var shortcut = shell.CreateShortcut(shortcutPath);

            shortcut.TargetPath = exePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
            shortcut.Description = "Launch Nuclear Option with Mods";
            shortcut.Save();

            NotificationService.Instance.Success("Shortcut created on desktop.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shortcut creation failed");
            NotificationService.Instance.Error("Failed to create shortcut.");
        }
    }

    public async Task RemoveBepInEx()
    {
        await Task.Run(() =>
        {
            var gameDir = GetGameDirectory();
            if (string.IsNullOrEmpty(gameDir)) return;

            string[] targets = { "BepInEx", "doorstop_config.ini", "winhttp.dll", "changelog.txt", "doorstop_libs" };
            foreach (var target in targets)
            {
                string path = Path.Combine(gameDir, target);
                try
                {
                    if (Directory.Exists(path)) Directory.Delete(path, true);
                    else if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception ex)
                {
                    Log.Warning("Could not delete {Path}: {Message}", path, ex.Message);
                }
            }
        });
    }
}