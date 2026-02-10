using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Yellowcake.Models;
using Yellowcake.Services;
using Yellowcake.Views;

namespace Yellowcake.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private bool _closeOnLaunch = true;
    [ObservableProperty] private bool _autoUpdateManifest = true;
    [ObservableProperty] private bool _autoUpdateMods;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _showNotifications = true;
    [ObservableProperty] private bool _isDarkMode = true;
    [ObservableProperty] private string _settingsShareCode = string.Empty;
    [ObservableProperty] private bool _isSettingsOpen;
    [ObservableProperty] private bool _isPerformanceDashboardOpen;
    [ObservableProperty] private bool _isLogViewerOpen;
    [ObservableProperty] private bool _autoLaunchGame;
    [ObservableProperty] private bool _useGpuAcceleration;
    [ObservableProperty] private bool _enableVerboseLogging;
    [ObservableProperty] private bool _useSecondaryManifest;
    [ObservableProperty] private bool _isHardwareAccelerationEnabled = true;
    [ObservableProperty] public string _selectedSourceName = "Primary Source";

    public bool IsOverlayOpen => IsSettingsOpen || IsPerformanceDashboardOpen || IsLogViewerOpen;

    public Dictionary<string, string> ManifestSources { get; } = new()
    {
        { "Primary Source", "https://gist.githubusercontent.com/NaghDiefallah/82544b5e011d78924b0ff7678e4180aa/raw/NOModsPrimary" },
        { "Secondary Source", "https://gist.githubusercontent.com/NaghDiefallah/82544b5e011d78924b0ff7678e4180aa/raw/NOModsSecondary" },
        { "Development Source (UNSTABLE)", "https://gist.githubusercontent.com/NaghDiefallah/82544b5e011d78924b0ff7678e4180aa/raw/NOModsTesting" },
        { "Community Source", "https://kopterbuzz.github.io/NOModManifestTesting/manifest/manifest.json" }
    };

    partial void OnIsSettingsOpenChanged(bool value) 
    {
        if (value)
        {
            IsPerformanceDashboardOpen = false;
            IsLogViewerOpen = false;
        }
        OnPropertyChanged(nameof(IsOverlayOpen));
    }

    partial void OnIsPerformanceDashboardOpenChanged(bool value)
    {
        if (value)
        {
            IsSettingsOpen = false;
            IsLogViewerOpen = false;
        }
        OnPropertyChanged(nameof(IsOverlayOpen));
    }

    partial void OnIsLogViewerOpenChanged(bool value)
    {
        if (value)
        {
            IsSettingsOpen = false;
            IsPerformanceDashboardOpen = false;
        }
        OnPropertyChanged(nameof(IsOverlayOpen));
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        IsSettingsOpen = !IsSettingsOpen;
    }

    [RelayCommand]
    private void TogglePerformanceDashboard()
    {
        IsPerformanceDashboardOpen = !IsPerformanceDashboardOpen;
    }

    [RelayCommand]
    private void ToggleLogViewer()
    {
        IsLogViewerOpen = !IsLogViewerOpen;
    }

    [RelayCommand]
    private void RestartApplication()
    {
        var currentProcess = Process.GetCurrentProcess();
        var fileName = currentProcess.MainModule?.FileName;
        if (fileName != null)
        {
            Process.Start(fileName);
            Environment.Exit(0);
        }
    }

    [RelayCommand] 
    private void ClearCache() => NotificationService.Instance.Info("Cache cleared");

    private void InitializeSettings()
    {
        CloseOnLaunch = GetBoolSetting("CloseOnLaunch", true);
        AutoUpdateManifest = GetBoolSetting("AutoUpdateManifest", true);
        AutoUpdateMods = GetBoolSetting("AutoUpdateMods", false);
        MinimizeToTray = GetBoolSetting("MinimizeToTray", false);
        ShowNotifications = GetBoolSetting("ShowNotifications", true);
        IsDarkMode = GetBoolSetting("IsDarkMode", true);

        var themes = _themeService.GetAvailableThemes();
        _availableThemes = new ObservableCollection<string>(themes);

        var savedTheme = _db.GetSetting(ThemeConfigKey);
        _selectedTheme = themes.FirstOrDefault(t => t == savedTheme) ??
                        themes.FirstOrDefault(t => t == "Dark") ??
                        themes.FirstOrDefault() ?? string.Empty;
    }

    [RelayCommand]
    public async Task GenerateShareCode()
    {
        try
        {
            var modLibrary = new ModLibrarySnapshot
            {
                ExportDate = DateTime.UtcNow,
                TotalMods = _installedMods.Count,
                Mods = _installedMods.Select(m => new ModSnapshot
                {
                    Id = m.Id,
                    Name = m.Name,
                    Version = m.Version,
                    Category = m.Category,
                    IsEnabled = m.IsEnabled
                }).ToList()
            };

            var json = JsonSerializer.Serialize(modLibrary, new JsonSerializerOptions 
            { 
                WriteIndented = false 
            });
            
            SettingsShareCode = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                var clipboard = TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(SettingsShareCode);
                    NotificationService.Instance.Success($"Mod library code copied ({_installedMods.Count} mods)");
                }
            }

            Log.Information("Generated share code for {Count} mods", _installedMods.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to generate share code");
            NotificationService.Instance.Error("Failed to generate share code");
        }
    }

    [RelayCommand]
    public async Task PasteShareCode()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            var clipboard = TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard;
            if (clipboard != null)
            {
                var text = await clipboard.GetTextAsync();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    SettingsShareCode = text;
                }
            }
        }
    }

    [RelayCommand]
    public async Task ApplyShareCode()
    {
        if (string.IsNullOrWhiteSpace(SettingsShareCode)) return;

        try
        {
            var decodedData = Encoding.UTF8.GetString(Convert.FromBase64String(SettingsShareCode.Trim()));
            var library = JsonSerializer.Deserialize<ModLibrarySnapshot>(decodedData);

            if (library == null || library.Mods == null || library.Mods.Count == 0)
            {
                NotificationService.Instance.Warning("Share code contains no mods");
                return;
            }

            bool confirmed = await NotificationService.Instance.ConfirmAsync(
                "Restore Mod Library",
                $"Install {library.Mods.Count} mod(s) from {library.ExportDate:yyyy-MM-dd}?",
                TimeSpan.FromSeconds(15));

            if (!confirmed) return;

            var installedIds = new HashSet<string>(_installedMods.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
            int queued = 0;

            foreach (var snapshot in library.Mods)
            {
                if (installedIds.Contains(snapshot.Id)) continue;

                var targetMod = _availableMods.FirstOrDefault(m => 
                    string.Equals(m.Id, snapshot.Id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(m.Name, snapshot.Name, StringComparison.OrdinalIgnoreCase));

                if (targetMod != null)
                {
                    await DownloadMod(targetMod);
                    queued++;
                }
            }

            NotificationService.Instance.Success($"Installed {queued} mod(s) from library");
            Log.Information("Applied share code: {Queued} mods installed", queued);
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "Invalid mod library share code");
            NotificationService.Instance.Error("Invalid share code format");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply share code");
            NotificationService.Instance.Error($"Failed to restore library: {ex.Message}");
        }
    }

    [RelayCommand]
    public void OpenThemesFolder()
    {
        try
        {
            string themesPath = Path.Combine(AppContext.BaseDirectory, "Themes");
            if (!Directory.Exists(themesPath)) Directory.CreateDirectory(themesPath);
            Process.Start(new ProcessStartInfo(themesPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open themes folder");
        }
    }

    [RelayCommand]
    public void RefreshThemes()
    {
        _availableThemes.Clear();
        foreach (var theme in _themeService.GetAvailableThemes())
        {
            _availableThemes.Add(theme);
        }
        NotificationService.Instance.Info("Themes refreshed");
    }

    [RelayCommand]
    public void ResetSettings()
    {
        CloseOnLaunch = true;
        AutoUpdateManifest = true;
        AutoUpdateMods = false;
        MinimizeToTray = false;
        ShowNotifications = true;
        IsDarkMode = true;
        SettingsShareCode = string.Empty;
        NotificationService.Instance.Info("Settings restored to defaults");
    }

    [RelayCommand]
    public async Task ExportModList()
    {
        var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var topLevel = TopLevel.GetTopLevel(desktop?.MainWindow);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Mod List",
            SuggestedFileName = $"Yellowcake-Library-{DateTime.Now:yyyy-MM-dd}.json",
            DefaultExtension = "json",
            FileTypeChoices = new[] 
            { 
                new FilePickerFileType("Mod Library") { Patterns = new[] { "*.json" } },
                FilePickerFileTypes.All 
            }
        });

        if (file is null) return;

        try
        {
            var library = new ModLibrarySnapshot
            {
                ExportDate = DateTime.UtcNow,
                TotalMods = _installedMods.Count,
                Mods = _installedMods.Select(m => new ModSnapshot
                {
                    Id = m.Id,
                    Name = m.Name,
                    Version = m.Version,
                    Category = m.Category,
                    IsEnabled = m.IsEnabled
                }).ToList()
            };

            var json = JsonSerializer.Serialize(library, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json);

            NotificationService.Instance.Success($"Exported {_installedMods.Count} mod(s) to file");
            Log.Information("Exported mod list: {Count} mods", _installedMods.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export mod list");
            NotificationService.Instance.Error("Failed to export mod list");
        }
    }

    partial void OnSelectedSourceChanged(KeyValuePair<string, string> value)
    {
    if (string.IsNullOrWhiteSpace(value.Value)) return;

    _manifestService.TargetUrl = value.Value;
    _db.SaveSetting("ManifestSourceFriendlyName", value.Key);

    Log.Information("Source switched to {Name}: {Url}", value.Key, value.Value);

    _ = Task.Run(async () =>
    {
        try
        {
            await LoadAvailableModsAsync();
            await Dispatcher.UIThread.InvokeAsync(RefreshUI);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to refresh mods after source change to {Name}", value.Key);
        }
    });
}

    [RelayCommand]
    public async Task ImportModList()
    {
        var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var topLevel = TopLevel.GetTopLevel(desktop?.MainWindow);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Mod List",
            AllowMultiple = false,
            FileTypeFilter = new[] 
            { 
                new FilePickerFileType("Mod Library") { Patterns = new[] { "*.json" } },
                FilePickerFileTypes.All 
            }
        });

        if (files.Count == 0) return;

        try
        {
            using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();

            var library = JsonSerializer.Deserialize<ModLibrarySnapshot>(json);

            if (library == null || library.Mods == null || library.Mods.Count == 0)
            {
                NotificationService.Instance.Warning("File contains no mods");
                return;
            }

            bool confirmed = await NotificationService.Instance.ConfirmAsync(
                "Import Mod Library",
                $"Install {library.Mods.Count} mod(s) from {library.ExportDate:yyyy-MM-dd}?",
                TimeSpan.FromSeconds(15));

            if (!confirmed) return;

            var installedIds = new HashSet<string>(_installedMods.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
            int queued = 0;

            foreach (var snapshot in library.Mods)
            {
                if (installedIds.Contains(snapshot.Id)) continue;

                var targetMod = _availableMods.FirstOrDefault(m => 
                    string.Equals(m.Id, snapshot.Id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(m.Name, snapshot.Name, StringComparison.OrdinalIgnoreCase));

                if (targetMod != null)
                {
                    await DownloadMod(targetMod);
                    queued++;
                }
            }

            NotificationService.Instance.Success($"Installed {queued} mod(s) from library");
            Log.Information("Imported mod list: {Queued} mods installed", queued);
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "Invalid mod library file format");
            NotificationService.Instance.Error("Invalid mod library file");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to import mod list");
            NotificationService.Instance.Error($"Import failed: {ex.Message}");
        }
    }

    partial void OnCloseOnLaunchChanged(bool value) => _db.SaveSetting("CloseOnLaunch", value.ToString());
    partial void OnAutoUpdateManifestChanged(bool value) => _db.SaveSetting("AutoUpdateManifest", value.ToString());
    partial void OnAutoUpdateModsChanged(bool value) => _db.SaveSetting("AutoUpdateMods", value.ToString());
    partial void OnMinimizeToTrayChanged(bool value) => _db.SaveSetting("MinimizeToTray", value.ToString());
    partial void OnShowNotificationsChanged(bool value) => _db.SaveSetting("ShowNotifications", value.ToString());
    partial void OnIsDarkModeChanged(bool value) => _db.SaveSetting("IsDarkMode", value.ToString());
}

public class ModLibrarySnapshot
{
    public DateTime ExportDate { get; set; }
    public int TotalMods { get; set; }
    public List<ModSnapshot> Mods { get; set; } = new();
}

public class ModSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}