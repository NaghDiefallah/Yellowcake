using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Octokit;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Yellowcake.Models;
using Yellowcake.Services;

namespace Yellowcake.ViewModels;

public partial class MainViewModel : ObservableRecipient
{
    private readonly BepInExService _bepInExService;
    private readonly DatabaseService _db;
    private readonly ModService _modService;
    private readonly InstallService _installService;
    private readonly ThemeService _themeService;
    private readonly PathService _pathService;
    private readonly HttpClient _http = new();
    private readonly GitHubClient _gh = new(new ProductHeaderValue("Yellowcake-Manager"));

    private List<Mod> _allRemoteMods = [];
    private const string CurrentVersion = "1.0.0";
    private const string ThemeConfigKey = "SelectedTheme";

    public string AppVersion => $"v{CurrentVersion}";

    public MainViewModel()
    {
        var root = AppContext.BaseDirectory;
        var vaultPath = Path.Combine(root, "Mods");

        Directory.CreateDirectory(vaultPath);

        _db = ThemeService.Database ?? new DatabaseService();
        _pathService = new PathService(_db);
        _installService = new InstallService(vaultPath, _pathService);

        _themeService = new ThemeService();
        _modService = new ModService(_db, _installService, _http, _gh);
        _bepInExService = new BepInExService();

        LoadPersistentData();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(InitializeAsync(), LoadBepVersionsAsync());
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Background initialization failed");
                GameStatus = "Initialization failed.";
            }
        });
    }

    private void LoadPersistentData()
    {
        var savedPath = _db.GetSetting("GamePath");

        if (!string.IsNullOrWhiteSpace(savedPath) && File.Exists(savedPath))
        {
            GamePath = savedPath;
            Log.Information("Path verified and loaded: {Path}", savedPath);
        }
        else if (!string.IsNullOrEmpty(savedPath))
        {
            Log.Warning("Saved path exists in DB but file was not found: {Path}", savedPath);
        }

        InitializeSettings();
    }

    private bool CanInstall() =>
        !string.IsNullOrWhiteSpace(SelectedBepInExVersion) &&
        !string.IsNullOrWhiteSpace(GamePath) &&
        BepInExDownloadProgress <= 0;

    private async Task LoadBepVersionsAsync()
    {
        try
        {
            var versions = await _bepInExService.GetVersionListAsync();

            if (versions == null || !versions.Any())
            {
                GameStatus = "No BepInEx versions found.";
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                BepInExVersions.Clear();
                foreach (var version in versions)
                    BepInExVersions.Add(version);

                SelectedBepInExVersion = BepInExVersions.FirstOrDefault();
            });
        }
        catch (HttpRequestException)
        {
            GameStatus = "Connection error: Could not reach GitHub.";
        }
        catch (Exception ex)
        {
            GameStatus = $"Error loading versions: {ex.Message}";
            Log.Error(ex, "Failed to load BepInEx versions");
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallSelectedVersionAsync()
    {
        try
        {
            IsSettingsOpen = false;
            GameStatus = $"Installing BepInEx {SelectedBepInExVersion}...";
            InstallSelectedVersionCommand.NotifyCanExecuteChanged();

            await _bepInExService.InstallVersionAsync(
                SelectedBepInExVersion!,
                GamePath!,
                progress => BepInExDownloadProgress = progress);

            OnPropertyChanged(nameof(IsBepInstalled));
            GameStatus = $"Successfully installed {SelectedBepInExVersion}";
        }
        catch (UnauthorizedAccessException)
        {
            GameStatus = "Access denied: Run as Administrator.";
        }
        catch (IOException)
        {
            GameStatus = "File error: Ensure the game is closed.";
        }
        catch (Exception ex)
        {
            GameStatus = $"Installation failed: {ex.Message}";
            Log.Error(ex, "BepInEx installation error");
        }
        finally
        {
            await Task.Delay(2000);
            BepInExDownloadProgress = 0;
            InstallSelectedVersionCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task InitializeAsync()
    {
        GameStatus = "Initializing...";

        try
        {
            GamePath = _db.GetSetting("GamePath");

            if (string.IsNullOrWhiteSpace(GamePath))
            {
                Log.Information("No game path found in database. Attempting auto-detection.");
                await AutoDetectGamePath();
            }

            await Task.WhenAll(
                CheckForAppUpdatesAsync(),
                LoadAvailableModsAsync()
            );

            await SyncInstalledStates();
            await RefreshUI();

            if (AutoUpdateMods && IsGameDetected)
            {
                Log.Information("Auto-updating mods as per user settings.");
                _ = UpdateAllMods();
            }

            GameStatus = IsGameDetected ? "Ready" : "Awaiting game path...";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Initialization phase encountered a failure.");
            GameStatus = "Initialization Error";
            NotificationService.Instance.Error("Failed to initialize the mod manager.");
        }
    }
}