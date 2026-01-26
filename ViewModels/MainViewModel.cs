using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Octokit;
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
using Yellowcake.Services;

namespace Yellowcake.ViewModels;

public partial class MainViewModel : ObservableRecipient
{
    private readonly ModService _modService;
    private readonly InstallService _installService;
    private readonly HttpClient _http = new();
    private readonly GitHubClient _gh = new(new ProductHeaderValue("Yellowcake-Manager"));
    private List<Mod> _allRemoteMods = [];

    [ObservableProperty] private string _gameStatus = "STATUS: OFFLINE";
    [ObservableProperty] private ObservableCollection<Mod> _modList = [];
    [ObservableProperty] private ObservableCollection<Mod> _availableMods = [];
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string? _gamePath;
    [ObservableProperty] private bool _isGameDetected;
    [ObservableProperty] private bool _isBepInstalled;
    [ObservableProperty] private double _bepInExDownloadProgress;

    public IEnumerable<Mod> VoicePacks => ModList.Where(m => m.IsVoicePack || m.Category == "Audio");
    public IEnumerable<Mod> Liveries => ModList.Where(m => m.IsLivery || m.Category == "Visual");
    public bool HasVoicePacks => VoicePacks.Any();
    public bool HasLiveries => Liveries.Any();

    public MainViewModel()
    {
        try
        {
            string root = AppContext.BaseDirectory;
            string vaultPath = Path.Combine(root, "Mods");
            string dbPath = Path.Combine(root, "data.db");

            if (!Directory.Exists(vaultPath)) Directory.CreateDirectory(vaultPath);

            var databaseService = new DatabaseService(dbPath);
            _installService = new InstallService(vaultPath);
            _modService = new ModService(databaseService, _installService, _http, _gh);

            ModList.CollectionChanged += (s, e) => RefreshTabVisibility();

            _ = InitializeAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Critical initialization failure");
        }
    }

    private async Task InitializeAsync()
    {
        string? savedPath = _modService.LoadGamePath();
        if (!string.IsNullOrEmpty(savedPath) && File.Exists(savedPath))
        {
            GamePath = savedPath;
            _modService.SetGamePath(savedPath);
        }
        else
        {
            GamePath = "Not Set";
        }

        await LoadAvailableModsAsync();
        await RefreshUI();
    }

    private async Task LoadAvailableModsAsync()
    {
        try
        {
            var mods = await _modService.FetchRemoteManifest();
            if (mods == null) return;

            _allRemoteMods = mods;
            FilterMods();
            _ = Task.Run(EnrichVersionsAsync);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Manifest sync failed");
            GameStatus = "MANIFEST SYNC FAILED";
        }
    }

    private async Task EnrichVersionsAsync()
    {
        using var semaphore = new SemaphoreSlim(3);
        var tasks = _allRemoteMods
            .Where(m => !string.IsNullOrWhiteSpace(m.GitHubUrl) && !m.GitHubUrl.Contains("/raw/"))
            .Select(async mod =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var uri = new Uri(mod.GitHubUrl!.Trim());
                    var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length < 2) return;

                    var release = await _gh.Repository.Release.GetLatest(segments[0], segments[1].Replace(".git", ""));

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        mod.LatestVersion = release.TagName;
                        var installed = ModList.FirstOrDefault(m => m.Id == mod.Id);
                        if (installed != null && installed.Version != release.TagName)
                        {
                            installed.HasUpdate = true;
                        }
                    });
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not fetch version for {Mod}: {Msg}", mod.Id, ex.Message);
                }
                finally { semaphore.Release(); }
            });

        await Task.WhenAll(tasks);
    }

    public async Task RefreshUI()
    {
        var installedMods = _modService.GetInstalledMods();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ModList.Clear();
            foreach (var mod in installedMods) ModList.Add(mod);

            IsGameDetected = !string.IsNullOrEmpty(GamePath) && GamePath != "Not Set";
            IsBepInstalled = _modService.IsBepInExInstalled();

            GameStatus = !IsGameDetected ? "AWAITING GAME PATH" :
                         (!IsBepInstalled ? "BEPINEX REQUIRED" : "READY FOR SORTIE");

            RefreshTabVisibility();
        });

        if (IsGameDetected && !IsBepInstalled && BepInExDownloadProgress == 0)
        {
            await PromptBepInEx();
        }
    }

    private void RefreshTabVisibility()
    {
        OnPropertyChanged(nameof(VoicePacks));
        OnPropertyChanged(nameof(Liveries));
        OnPropertyChanged(nameof(HasVoicePacks));
        OnPropertyChanged(nameof(HasLiveries));
    }

    private async Task PromptBepInEx()
    {
        var result = await MessageBoxManager.GetMessageBoxStandard(
            "BepInEx Required",
            "BepInEx is required for plugins. Install it now?",
            ButtonEnum.YesNo,
            Icon.Info).ShowAsync();

        if (result == ButtonResult.Yes) await RunBepInExInstallation();
    }

    private async Task RunBepInExInstallation()
    {
        string? targetDir = Path.GetDirectoryName(GamePath);
        if (string.IsNullOrEmpty(targetDir)) return;

        try
        {
            var progress = new Progress<double>(p => BepInExDownloadProgress = p);
            await _modService.InstallBepInExAsync(targetDir, progress);
            await RefreshUI();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "BepInEx installation failed");
            NotificationService.Instance.Error("Failed to install BepInEx.");
        }
        finally
        {
            BepInExDownloadProgress = 0;
        }
    }

    partial void OnSearchTextChanged(string? value) => FilterMods();

    private void FilterMods()
    {
        var query = SearchText ?? string.Empty;
        var filtered = _allRemoteMods
            .Where(m => string.IsNullOrWhiteSpace(query) ||
                        m.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        AvailableMods.Clear();
        foreach (var mod in filtered) AvailableMods.Add(mod);
    }

    [RelayCommand]
    public async Task UpdateAllMods()
    {
        var toUpdate = ModList.Where(m => m.HasUpdate).ToList();
        if (!toUpdate.Any()) return;

        try
        {
            foreach (var mod in toUpdate)
            {
                GameStatus = $"UPDATING: {mod.Name.ToUpperInvariant()}";
                var remote = _allRemoteMods.FirstOrDefault(m => m.Id == mod.Id);
                if (remote != null) await _modService.DownloadAndInstallMod(remote, ModList, _allRemoteMods);
            }
            await RefreshUI();
            NotificationService.Instance.Success("All mods updated successfully.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Update failed");
            GameStatus = "UPDATE FAILED";
        }
    }
}