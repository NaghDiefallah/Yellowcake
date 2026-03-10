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
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Yellowcake.Models;
using Yellowcake.Services;

namespace Yellowcake.ViewModels;

public partial class MainViewModel : ObservableRecipient, IDisposable
{
    private readonly BepInExService _bepInExService;
    private readonly DatabaseService _db;
    private readonly ModService _modService;
    private readonly InstallService _installService;
    private readonly ThemeService _themeService;
    private readonly PathService _pathService;
    private readonly HttpClient _http = new();
    private readonly GitHubClient _gh = new(new ProductHeaderValue("Yellowcake-Manager"));
    private readonly ManifestService _manifestService;
    private readonly PerformanceTracker _performanceTracker;

    private readonly DownloadQueue _downloadQueue;

    private readonly CancellationTokenSource _shutdownCts;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyMessage = "Loading...";

    [ObservableProperty] private bool _canCancel;

    private List<Mod> _allRemoteMods = new();
    private string CurrentVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(4) ?? "1.0.0.0";
    private const string ThemeConfigKey = "SelectedTheme";

    public string AppVersion => $"v{CurrentVersion}";

    public DownloadQueue DownloadQueue => _downloadQueue;

    public MainViewModel(
        DatabaseService db,
        InstallService? installService,
        ModService? modService,
        ManifestService? manifestService,
        DownloadQueue downloadQueue,
        CancellationTokenSource shutdownCts,
        HttpClient http,
        GitHubClient gh,
        ThemeService? themeService = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _pathService = new PathService(_db);
        _installService = installService ?? new InstallService(_pathService);
        _modService = modService ?? new ModService(_db, _installService, http, gh);
        _manifestService = manifestService ?? new ManifestService(http);
        _downloadQueue = downloadQueue ?? new DownloadQueue(maxParallel: 4);
        _shutdownCts = shutdownCts ?? new CancellationTokenSource();
        _http = http ?? new HttpClient();
        _gh = gh ?? new GitHubClient(new ProductHeaderValue("Yellowcake-Manager"));
        _themeService = themeService ?? new ThemeService();
        _bepInExService = new BepInExService();
        _performanceTracker = new PerformanceTracker(_db);
    }

    // In the default constructor, make sure to initialize the command there too
    public MainViewModel()
        : this(
            db: ThemeService.Database ?? new DatabaseService(),
            installService: null,
            modService: null,
            manifestService: null,
            downloadQueue: new DownloadQueue(maxParallel: 4),
            shutdownCts: new CancellationTokenSource(),
            http: new HttpClient(),
            gh: new GitHubClient(new ProductHeaderValue("Yellowcake-Manager")),
            themeService: new ThemeService())
    {
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
            Log.Information("Loading BepInEx versions from GitHub...");
            
            var versions = await _bepInExService.GetVersionListAsync();

            if (versions == null || !versions.Any())
            {
                Log.Warning("No BepInEx versions found");
                GameStatus = "No BepInEx versions found.";
                NotificationService.Instance.Warning("Could not load BepInEx versions");
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                BepInExVersions.Clear();
                foreach (var version in versions)
                    BepInExVersions.Add(version);

                SelectedBepInExVersion = BepInExVersions.FirstOrDefault();
                
                InstallSelectedVersionCommand.NotifyCanExecuteChanged();
                
                Log.Information("Loaded {Count} BepInEx versions", versions.Count);
            });

            // Auto-install BepInEx if not already installed and game path is set
            if (!IsBepInstalled && !string.IsNullOrWhiteSpace(GamePath) && GamePath != "Not Set" && !string.IsNullOrWhiteSpace(SelectedBepInExVersion))
            {
                Log.Information("BepInEx not detected. Auto-installing latest version: {Version}", SelectedBepInExVersion);
                NotificationService.Instance.Info($"Auto-installing BepInEx {SelectedBepInExVersion}...");
                await AutoInstallBepInExAsync();
            }
        }
        catch (HttpRequestException ex)
        {
            Log.Error(ex, "Failed to connect to GitHub");
            GameStatus = "Connection error: Could not reach GitHub.";
            NotificationService.Instance.Warning("Could not connect to GitHub. Check your internet connection.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load BepInEx versions");
            GameStatus = $"Error loading versions: {ex.Message}";
            NotificationService.Instance.Error("Failed to load BepInEx versions");
        }
    }

    private async Task AutoInstallBepInExAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedBepInExVersion) || string.IsNullOrWhiteSpace(GamePath) || GamePath == "Not Set")
        {
            Log.Warning("Cannot auto-install BepInEx: missing version or game path");
            return;
        }

        try
        {
            BusyMessage = $"Auto-installing BepInEx {SelectedBepInExVersion}...";
            GameStatus = $"Installing BepInEx {SelectedBepInExVersion}...";

            await _bepInExService.InstallVersionAsync(
                SelectedBepInExVersion!,
                GamePath!,
                progress =>
                {
                    BepInExDownloadProgress = progress;
                    Log.Debug("BepInEx auto-install progress: {Progress}%", progress);
                });

            Log.Information("BepInEx auto-installation completed successfully");
            
            await RefreshUI();
            OnPropertyChanged(nameof(IsBepInstalled));
            GameStatus = "Ready";
            
            NotificationService.Instance.Success($"BepInEx {SelectedBepInExVersion} installed successfully!");
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Error(ex, "BepInEx auto-install: Access denied");
            GameStatus = "BepInEx auto-install failed: Access denied";
            Log.Warning("BepInEx auto-install failed due to access restrictions. User can retry manually.");
        }
        catch (IOException ex)
        {
            Log.Error(ex, "BepInEx auto-install: File error");
            GameStatus = "BepInEx auto-install failed: File error";
            Log.Warning("BepInEx auto-install failed due to file issues. User can retry manually.");
        }
        catch (HttpRequestException ex)
        {
            Log.Error(ex, "BepInEx auto-install: Network error");
            GameStatus = "BepInEx auto-install failed: Network error";
            Log.Warning("BepInEx auto-install failed due to network issues. User can retry manually.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "BepInEx auto-install error");
            GameStatus = "BepInEx auto-install failed";
            Log.Warning("BepInEx auto-install failed. User can retry manually.");
        }
        finally
        {
            await Task.Delay(2000);
            BepInExDownloadProgress = 0;
            
            if (GameStatus.StartsWith("Error") || GameStatus.Contains("failed"))
            {
                await Task.Delay(3000);
                GameStatus = IsGameDetected ? "Ready" : "Awaiting game path...";
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallSelectedVersionAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedBepInExVersion))
        {
            NotificationService.Instance.Warning("Please select a BepInEx version first");
            return;
        }

        if (string.IsNullOrWhiteSpace(GamePath) || GamePath == "Not Set")
        {
            NotificationService.Instance.Error("Game path not set. Please set it in Settings.");
            return;
        }

        try
        {
            IsSettingsOpen = false;
            IsBusy = true;
            BusyMessage = $"Installing BepInEx {SelectedBepInExVersion}...";
            GameStatus = $"Installing BepInEx {SelectedBepInExVersion}...";
            InstallSelectedVersionCommand.NotifyCanExecuteChanged();

            Log.Information("Starting BepInEx installation: {Version}", SelectedBepInExVersion);

            await _bepInExService.InstallVersionAsync(
                SelectedBepInExVersion!,
                GamePath!,
                progress =>
                {
                    BepInExDownloadProgress = progress;
                    Log.Debug("BepInEx download progress: {Progress}%", progress);
                });

            Log.Information("BepInEx installation completed successfully");

            await RefreshUI();
            
            OnPropertyChanged(nameof(IsBepInstalled));
            GameStatus = "Ready";
            
            NotificationService.Instance.Success($"BepInEx {SelectedBepInExVersion} installed successfully!");
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Error(ex, "BepInEx installation: Access denied");
            GameStatus = "Error: Access denied";
            NotificationService.Instance.Error(
                "Access denied. Try running as Administrator or ensure the game is not running.",
                () => InstallSelectedVersionCommand.Execute(null)
            );
        }
        catch (IOException ex)
        {
            Log.Error(ex, "BepInEx installation: File error");
            GameStatus = "Error: File error";
            NotificationService.Instance.Error(
                "File error. Please ensure the game is closed and try again.",
                () => InstallSelectedVersionCommand.Execute(null)
            );
        }
        catch (HttpRequestException ex)
        {
            Log.Error(ex, "BepInEx installation: Network error");
            GameStatus = "Error: Network error";
            NotificationService.Instance.Error(
                $"Network error: {ex.Message}. Check your internet connection.",
                () => InstallSelectedVersionCommand.Execute(null)
            );
        }
        catch (InvalidOperationException ex)
        {
            Log.Error(ex, "BepInEx installation: Invalid operation");
            GameStatus = "Error: Installation failed";
            NotificationService.Instance.Error(
                $"Installation failed: {ex.Message}",
                () => InstallSelectedVersionCommand.Execute(null)
            );
        }
        catch (Exception ex)
        {
            Log.Error(ex, "BepInEx installation error");
            GameStatus = "Error";
            NotificationService.Instance.Error(
                $"Installation failed: {ex.Message}",
                () => InstallSelectedVersionCommand.Execute(null)
            );
        }
        finally
        {
            await Task.Delay(2000);
            BepInExDownloadProgress = 0;
            IsBusy = false;
            InstallSelectedVersionCommand.NotifyCanExecuteChanged();
            
            if (GameStatus.StartsWith("Error"))
            {
                await Task.Delay(3000);
                GameStatus = IsGameDetected ? "Ready" : "Awaiting game path...";
            }
        }
    }

    private async Task AutoDetectGamePath()
    {
        try
        {
            string exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                ? "NuclearOption.exe" 
                : "NuclearOption";

            var steamPaths = new[]
            {
                @"C:\Program Files (x86)\Steam\steamapps\common\NuclearOption",
                @"C:\Program Files\Steam\steamapps\common\NuclearOption",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steamapps", "common", "NuclearOption"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common", "NuclearOption")
            };

            foreach (var steamPath in steamPaths)
            {
                var exePath = Path.Combine(steamPath, exeName);
                if (File.Exists(exePath))
                {
                    GamePath = exePath;
                    _db.SaveSetting("GamePath", exePath);
                    Log.Information("Auto-detected game at: {Path}", exePath);
                    NotificationService.Instance.Success("Game detected automatically!");
                    return;
                }
            }

            Log.Information("Auto-detection failed: Game not found in common locations");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Auto-detection error");
        }
    }

    private async Task InitializeAsync()
    {
        IsBusy = true;
        BusyMessage = "Initializing Mod Manager...";
        GameStatus = "Initializing...";

        try
        {
            var source = ManifestSources.First();
            _manifestService.TargetUrl = source.Value;
            SelectedSourceName = source.Key;
            _db.SaveSetting("ManifestSourceFriendlyName", source.Key);
            OnPropertyChanged(nameof(SelectedSource));

            GamePath = _db.GetSetting("GamePath") ?? "Not Set";
            if (string.IsNullOrWhiteSpace(GamePath) || GamePath == "Not Set")
            {
                BusyMessage = "Auto-detecting game path...";
                await AutoDetectGamePath();
            }

            BusyMessage = "Loading mods...";
            await LoadAvailableModsAsync();

            BusyMessage = "Repairing installed mod layout...";
            var repairedLayouts = await _modService.RepairStoredModLayoutsAsync(_shutdownCts.Token);
            if (repairedLayouts > 0)
            {
                NotificationService.Instance.Info($"Repaired {repairedLayouts} mod folder layout(s).");
            }

            _ = Task.Run(CheckForAppUpdatesAsync);

            await SyncInstalledStates();
            await RefreshUI();

            if (AutoUpdateMods && IsGameDetected)
            {
                _ = UpdateAllMods();
            }

            GameStatus = IsGameDetected ? "Ready" : "Awaiting game path...";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Initialization failed.");
            GameStatus = "Initialization Error";
            NotificationService.Instance.Error("Failed to initialize the mod manager.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        try
        {
            _shutdownCts?.Cancel();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error while cancelling shutdown token.");
        }

        try
        {
            var fi = this.GetType().GetField("_activeDownloads", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (fi != null && fi.GetValue(this) is Dictionary<string, CancellationTokenSource> dict)
            {
                lock (dict)
                {
                    foreach (var kv in dict.ToList())
                    {
                        try
                        {
                            kv.Value.Cancel();
                            kv.Value.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Log.Debug(ex, "Error cancelling active download {Id}", kv.Key);
                        }
                    }
                    dict.Clear();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to cancel active downloads.");
        }

        try
        {
            _http?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed disposing HttpClient.");
        }

        try
        {
            _shutdownCts?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed disposing shutdown CancellationTokenSource.");
        }
    }

    public DatabaseService GetDatabase() => _db;
    public InstallService GetInstallService() => _installService;
    public ModService GetModService() => _modService;
}