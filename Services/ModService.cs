using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Yellowcake.Models;
using Octokit;
using Avalonia.Threading;

namespace Yellowcake.Services;

public class ModService
{
    private readonly DatabaseService _db;
    private readonly InstallService _installer;
    private readonly PathService _pathService;
    private readonly ManifestService _manifests;
    private readonly DownloadService _downloader;

    public ModService(DatabaseService db, InstallService installer, HttpClient client, GitHubClient github)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _pathService = new PathService(db);
        _manifests = new ManifestService(client);
        _downloader = new DownloadService(client, github);

        Log.Information("ModService initialized.");
    }

    public string? LoadGamePath() => _db.GetSetting("GamePath");

    public void SaveGamePath(string path)
    {
        Log.Information("Updating game path to: {Path}", path);
        _db.SaveSetting("GamePath", path);
        _pathService.GamePath = path;
    }

    public void SetGamePath(string path)
    {
        _pathService.SetGamePath(path);
        _pathService.GamePath = path;
    }

    public List<Mod> GetInstalledMods() => _db.GetAllMods();

    public bool IsBepInExInstalled()
    {
        var gameDir = GetGameDirectory();
        return !string.IsNullOrEmpty(gameDir) && Directory.Exists(Path.Combine(gameDir, "BepInEx"));
    }

    public async Task InstallBepInExAsync(string targetDir, IProgress<double> progress)
    {
        if (!Directory.Exists(targetDir))
            throw new DirectoryNotFoundException($"Target directory not found: {targetDir}");

        const string url = "https://github.com/BepInEx/BepInEx/releases/download/v5.4.21/BepInEx_x64_5.4.21.0.zip";
        Log.Information("Downloading BepInEx framework...");

        using var stream = await _downloader.DownloadWithProgress(url, progress.Report);
        await _installer.ExtractBepInEx(stream, targetDir);
    }

    public Task<List<Mod>> FetchRemoteManifest() => _manifests.FetchRemoteManifest();

    public async Task DownloadAndInstallMod(Mod mod, ObservableCollection<Mod> localList, List<Mod> manifest, CancellationToken ct = default)
    {
        if (mod == null) throw new ArgumentNullException(nameof(mod));

        Log.Information("Preparing installation for: {ModName}", mod.Name);
        await InstallDependencies(mod, localList, manifest, ct);

        try
        {
            var (downloadUrl, tag) = await ResolveDownloadInfo(mod);
            if (string.IsNullOrEmpty(downloadUrl))
            {
                Log.Warning("No download URL resolved for {ModName}", mod.Name);
                return;
            }

            using var stream = await _downloader.DownloadWithProgress(downloadUrl, p => mod.DownloadProgress = p, ct);
            await ProcessModFiles(stream, downloadUrl, mod, tag, ct);

            UpdateLocalList(localList, mod);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex, "Installation failed for {Mod}", mod.Name);
            NotificationService.Instance.Error($"Could not install {mod.Name}");
            throw;
        }
    }

    private async Task<(string? url, string tag)> ResolveDownloadInfo(Mod mod)
    {
        if (string.IsNullOrWhiteSpace(mod.GitHubUrl)) return (null, mod.Version ?? "1.0.0");

        string url = mod.GitHubUrl;

        if (url.Contains("/raw/") || url.EndsWith(".7z") || url.EndsWith(".zip") || url.EndsWith(".rar") || url.EndsWith(".dll"))
        {
            return (url, mod.Version ?? "1.0.0");
        }

        try
        {
            var uri = new Uri(url.TrimEnd('/'));
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length >= 2)
            {
                var owner = segments[0];
                var repo = segments[1].Replace(".git", "");
                return await _downloader.GetLatestReleaseInfo(owner, repo);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to resolve GitHub info for {ModName}", mod.Name);
        }

        return (url, mod.Version ?? "1.0.0");
    }

    private async Task ProcessModFiles(MemoryStream stream, string url, Mod mod, string tag, CancellationToken ct)
    {
        var gameDir = GetGameDirectory();

        if (string.IsNullOrEmpty(mod.Id))
            mod.Id = Guid.NewGuid().ToString();

        if (mod.Id.Contains("Yappinator", StringComparison.OrdinalIgnoreCase))
            mod.Id = "WSOYappinator";

        mod.Version = tag;
        mod.IsEnabled = true;

        await _installer.InstallCategorizedMod(mod, stream, gameDir);

        _db.RegisterMod(mod);
        ToggleMod(mod.Id, true);
    }

    public void ToggleMod(string id, bool enable)
    {
        var gameDir = GetGameDirectory();
        var mod = _db.GetAllMods().FirstOrDefault(m => m.Id == id);
        if (mod == null || string.IsNullOrEmpty(gameDir)) return;

        bool isStandardMod = !mod.IsVoicePack && mod.Category != "Audio" && !mod.IsLivery;
        if (isStandardMod)
        {
            _installer.LinkMod(id, gameDir, enable);
        }

        _db.UpdateModEnabled(id, enable);
    }

    public void DeleteMod(string id)
    {
        ToggleMod(id, false);
        _installer.CleanupMods(id);
        _db.DeleteMod(id);
    }

    private async Task InstallDependencies(Mod mod, ObservableCollection<Mod> local, List<Mod> manifest, CancellationToken ct)
    {
        if (mod.Dependencies == null || !mod.Dependencies.Any()) return;

        foreach (var depId in mod.Dependencies)
        {
            if (ct.IsCancellationRequested) break;

            bool isInstalled = local.Any(m => string.Equals(m.Id, depId, StringComparison.OrdinalIgnoreCase));
            if (isInstalled) continue;

            var dep = manifest?.FirstOrDefault(m => string.Equals(m.Id, depId, StringComparison.OrdinalIgnoreCase));
            if (dep != null)
            {
                await DownloadAndInstallMod(dep, local, manifest, ct);
            }
        }
    }

    private string GetGameDirectory()
    {
        var path = _pathService.GamePath ?? LoadGamePath();
        if (string.IsNullOrEmpty(path)) return string.Empty;

        return File.Exists(path) ? Path.GetDirectoryName(path) ?? string.Empty : path;
    }

    private void UpdateLocalList(ObservableCollection<Mod> localList, Mod mod)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var existing = localList.FirstOrDefault(m => m.Id == mod.Id);
            if (existing != null)
            {
                int index = localList.IndexOf(existing);
                localList[index] = mod;
            }
            else
            {
                localList.Add(mod);
            }
        });
    }
}