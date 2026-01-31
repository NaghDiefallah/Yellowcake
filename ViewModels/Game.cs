using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Yellowcake.Services;

namespace Yellowcake.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    public async Task SelectExe()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow?.StorageProvider is not { } storage)
            return;

        var result = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select NuclearOption.exe",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
            new FilePickerFileType("Nuclear Option Executable (NuclearOption.exe)")
            {
                Patterns = new[] { "NuclearOption.exe" },
                MimeTypes = new[] { "application/x-msdos-program" }
            }
        }
        });

        var file = result.FirstOrDefault();
        if (file == null) return;

        string path = file.Path.LocalPath;
        string fileName = Path.GetFileName(path);

        if (!string.Equals(fileName, "NuclearOption.exe", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning("Invalid file selection: {FileName}", fileName);
            await MessageBoxManager.GetMessageBoxStandard("Invalid File",
                "Please select the specific 'NuclearOption.exe' file to continue.",
                ButtonEnum.Ok, Icon.Warning).ShowAsync();
            return;
        }

        try
        {
            GamePath = path;
            _db.SaveSetting("GamePath", path);

            Log.Information("Validated and saved game path: {Path}", path);
            NotificationService.Instance.Success("Game path verified and saved.");

            await SyncInstalledStates();
            await RefreshUI();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update game path: {Path}", path);
            NotificationService.Instance.Error("Failed to save game path.");
        }
    }

    [RelayCommand]
    public async Task AutoDetectGamePath()
    {
        await Task.Run(async () =>
        {
            try
            {
                Log.Information("Scanning for Nuclear Option installation...");

                var libraryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string? steamBase = null;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    steamBase = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                                ?? Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Valve\Steam", "InstallPath", null) as string;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    steamBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/Steam");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    steamBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support/Steam");
                }

                if (!string.IsNullOrWhiteSpace(steamBase) && Directory.Exists(steamBase))
                {
                    var rootSteamApps = Path.Combine(steamBase, "steamapps");
                    libraryPaths.Add(rootSteamApps);

                    string vdfPath = Path.Combine(rootSteamApps, "libraryfolders.vdf");
                    if (File.Exists(vdfPath))
                    {
                        string vdfContent = File.ReadAllText(vdfPath);
                        var matches = System.Text.RegularExpressions.Regex.Matches(vdfContent, @"""path""\s+""([^""]+)""");
                        foreach (System.Text.RegularExpressions.Match match in matches)
                        {
                            string escapedPath = match.Groups[1].Value.Replace(@"\\", Path.DirectorySeparatorChar.ToString());
                            libraryPaths.Add(Path.Combine(escapedPath, "steamapps"));
                        }
                    }
                }

                string exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "NuclearOption.exe" : "NuclearOption";

                foreach (var lib in libraryPaths)
                {
                    if (!Directory.Exists(lib)) continue;

                    string manifestPath = Path.Combine(lib, "appmanifest_2168680.acf");
                    string installDir = Path.Combine(lib, "common", "Nuclear Option", exeName);

                    if (File.Exists(installDir))
                    {
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            GamePath = installDir;
                            _db.SaveSetting("GamePath", installDir);

                            Log.Information("Game detected: {Path}", installDir);
                            NotificationService.Instance.Success("Game auto-detected successfully!");

                            await SyncInstalledStates();
                            await RefreshUI();
                        });
                        return;
                    }
                }

                Log.Warning("Nuclear Option was not found in detected libraries.");
                NotificationService.Instance.Warning("Auto-detection failed. Please select the game manually.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during cross-platform auto-detection.");
            }
        });
    }

    [RelayCommand]
    public void LaunchGame()
    {
        if (!IsGameDetected || !File.Exists(GamePath))
        {
            NotificationService.Instance.Warning("Game executable not found.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(GamePath)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(GamePath)
            });

            if (CloseOnLaunch)
                Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to launch game at {Path}", GamePath);
            NotificationService.Instance.Error("Could not launch game.");
        }
    }

    [RelayCommand]
    public async Task InstallBepInEx()
    {
        if (!IsGameDetected) return;

        try
        {
            GameStatus = "Installing BepInEx...";
            string? gameDir = Path.GetDirectoryName(GamePath);

            if (string.IsNullOrEmpty(gameDir)) return;

            await _modService.InstallBepInExAsync(gameDir, new Progress<double>(p => BepInExDownloadProgress = p));

            await RefreshUI();
            NotificationService.Instance.Success("BepInEx installed successfully.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "BepInEx installation failed");
            NotificationService.Instance.Error("Installation failed.");
        }
        finally
        {
            BepInExDownloadProgress = 0;
            GameStatus = IsGameDetected ? "Ready" : "Awaiting game path...";
        }
    }

    [RelayCommand]
    public async Task UninstallBepInEx()
    {
        var box = MessageBoxManager.GetMessageBoxStandard(
            "Confirm Uninstall",
            "This will remove all BepInEx files. Installed mods will no longer function.",
            ButtonEnum.YesNo,
            Icon.Warning);

        if (await box.ShowAsync() == ButtonResult.Yes)
        {
            try
            {
                await _modService.RemoveBepInEx();
                await RefreshUI();
                NotificationService.Instance.Info("Modding files removed.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to remove BepInEx");
                NotificationService.Instance.Error("Cleanup failed.");
            }
        }
    }

    [RelayCommand]
    public void OpenModFolder()
    {
        if (Directory.Exists(_installService.ModsPath))
        {
            Process.Start(new ProcessStartInfo(_installService.ModsPath) { UseShellExecute = true });
        }
    }

    [RelayCommand]
    public void CreateShortcut()
    {
        if (string.IsNullOrEmpty(GamePath) || !File.Exists(GamePath))
        {
            NotificationService.Instance.Warning("Please select the game path first.");
            return;
        }

        try
        {
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string shortcutLocation = Path.Combine(desktopPath, "Nuclear Option.lnk");

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) throw new Exception("WScript.Shell COM interface not found.");

            dynamic shell = Activator.CreateInstance(shellType)!;
            var shortcut = shell.CreateShortcut(shortcutLocation);

            shortcut.TargetPath = GamePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(GamePath);
            shortcut.Description = "Launch Nuclear Option via Yellowcake";
            shortcut.IconLocation = $"{GamePath},0";

            shortcut.Save();

            Log.Information("Shortcut created at: {Path}", shortcutLocation);
            NotificationService.Instance.Success("Desktop shortcut created.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shortcut creation failed.");
            NotificationService.Instance.Error("Could not create shortcut.");
        }
    }
}