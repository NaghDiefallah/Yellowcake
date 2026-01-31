using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
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
using System.Threading.Tasks;
using Yellowcake.Services;

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
    [ObservableProperty] private bool _autoLaunchGame;
    [ObservableProperty] private bool _useGpuAcceleration;
    [ObservableProperty] private bool _enableVerboseLogging;
    [ObservableProperty] private bool _useSecondaryManifest;
    [ObservableProperty] private ObservableCollection<string> _bepInExVersions = new();
    [ObservableProperty] private bool _isHardwareAccelerationEnabled = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallSelectedVersionCommand))]
    private string? _selectedBepInExVersion;

    public IAsyncRelayCommand InstallSelectedBepInExCommand { get; }

    [RelayCommand]
    private void ToggleSettings()
    {
        IsSettingsOpen = !IsSettingsOpen;
    }

    [RelayCommand]
    private void RestartApplication()
    {
        var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
        System.Diagnostics.Process.Start(currentProcess.MainModule.FileName);
        Environment.Exit(0);
    }

    [RelayCommand] private void ClearCache() => NotificationService.Instance.Info("Cache cleared.");

    private void InitializeSettings()
    {
        CloseOnLaunch = GetBoolSetting("CloseOnLaunch", true);
        AutoUpdateManifest = GetBoolSetting("AutoUpdateManifest", true);
        AutoUpdateMods = GetBoolSetting("AutoUpdateMods", false);
        MinimizeToTray = GetBoolSetting("MinimizeToTray", false);
        ShowNotifications = GetBoolSetting("ShowNotifications", true);
        IsDarkMode = GetBoolSetting("IsDarkMode", true);

        var themes = _themeService.GetAvailableThemes();
        AvailableThemes = new ObservableCollection<string>(themes);

        var savedTheme = _db.GetSetting(ThemeConfigKey);
        SelectedTheme = themes.FirstOrDefault(t => t == savedTheme) ??
                        themes.FirstOrDefault(t => t == "Dark") ??
                        themes.FirstOrDefault() ?? string.Empty;
    }

    [RelayCommand]
    public async Task GenerateShareCode()
    {
        try
        {
            var settings = $"{CloseOnLaunch}|{AutoUpdateManifest}|{AutoUpdateMods}|{MinimizeToTray}|{ShowNotifications}|{IsDarkMode}";
            SettingsShareCode = Convert.ToBase64String(Encoding.UTF8.GetBytes(settings));

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                await desktop.MainWindow!.Clipboard!.SetTextAsync(SettingsShareCode);
                NotificationService.Instance.Success("Settings code copied to clipboard.");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to generate share code.");
        }
    }

    [RelayCommand]
    public async Task PasteShareCode()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var text = await desktop.MainWindow!.Clipboard!.GetTextAsync();
            if (!string.IsNullOrWhiteSpace(text))
            {
                SettingsShareCode = text;
                ApplyShareCode();
            }
        }
    }

    [RelayCommand]
    public void ApplyShareCode()
    {
        if (string.IsNullOrWhiteSpace(SettingsShareCode)) return;

        try
        {
            var decodedData = Encoding.UTF8.GetString(Convert.FromBase64String(SettingsShareCode.Trim()));
            var parts = decodedData.Split('|');

            if (parts.Length == 6)
            {
                CloseOnLaunch = bool.Parse(parts[0]);
                AutoUpdateManifest = bool.Parse(parts[1]);
                AutoUpdateMods = bool.Parse(parts[2]);
                MinimizeToTray = bool.Parse(parts[3]);
                ShowNotifications = bool.Parse(parts[4]);
                IsDarkMode = bool.Parse(parts[5]);
                NotificationService.Instance.Success("Settings applied.");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Invalid share code.");
            NotificationService.Instance.Error("The settings code is invalid.");
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
            Log.Error(ex, "Failed to open themes folder.");
        }
    }

    [RelayCommand]
    public void RefreshThemes()
    {
        AvailableThemes.Clear();
        foreach (var theme in _themeService.GetAvailableThemes())
        {
            AvailableThemes.Add(theme);
        }
        NotificationService.Instance.Info("Themes refreshed.");
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
        NotificationService.Instance.Info("Settings restored to defaults.");
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
            SuggestedFileName = "Yellowcake-Mod-List.txt",
            DefaultExtension = "txt",
            FileTypeChoices = new[] { FilePickerFileTypes.TextPlain }
        });

        if (file is null) return;

        var sb = new StringBuilder();
        sb.AppendLine("YELLOWCAKE MOD LIST EXPORT");
        sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine(new string('-', 30));

        foreach (var mod in InstalledMods)
        {
            sb.AppendLine($"[{mod.Category}] {mod.Name} by {mod.Author}");
        }

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(sb.ToString());
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
            FileTypeFilter = new[] { FilePickerFileTypes.TextPlain }
        });

        if (files.Count == 0) return;

        using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        int importCount = 0;

        var installedNames = new HashSet<string>(InstalledMods.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);

        while (await reader.ReadLineAsync() is { } line)
        {
            if (!line.Contains('[') || !line.Contains(']')) continue;

            try
            {
                var namePart = line.Split(']', 2)[1].Split(" by ", 2)[0].Trim();

                if (installedNames.Contains(namePart)) continue;

                var targetMod = AvailableMods.FirstOrDefault(m => m.Name.Equals(namePart, StringComparison.OrdinalIgnoreCase));
                if (targetMod != null)
                {
                    DownloadModCommand.Execute(targetMod);
                    importCount++;
                }
            }
            catch { continue; }
        }

        NotificationService.Instance.Info($"Queued {importCount} mods for installation.");
    }

    partial void OnSelectedThemeChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            _db.SaveSetting(ThemeConfigKey, value);
            _themeService.ApplyTheme(value);
        }
    }

    partial void OnCloseOnLaunchChanged(bool value) => _db.SaveSetting("CloseOnLaunch", value.ToString());
    partial void OnAutoUpdateManifestChanged(bool value) => _db.SaveSetting("AutoUpdateManifest", value.ToString());
    partial void OnAutoUpdateModsChanged(bool value) => _db.SaveSetting("AutoUpdateMods", value.ToString());
    partial void OnMinimizeToTrayChanged(bool value) => _db.SaveSetting("MinimizeToTray", value.ToString());
    partial void OnShowNotificationsChanged(bool value) => _db.SaveSetting("ShowNotifications", value.ToString());
    partial void OnIsDarkModeChanged(bool value) => _db.SaveSetting("IsDarkMode", value.ToString());
}