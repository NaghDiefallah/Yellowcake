using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Serilog;
using System;
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

            Log.Information("Game path selected: {Path}", path);

            await RefreshUI();
            NotificationService.Instance.Success("Game detected successfully!");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to set game path");
            NotificationService.Instance.Error("Failed to set game path.");
        }
    }

    [RelayCommand]
    public async Task BrowseForGame()
    {
        await SelectExe();
    }

    [RelayCommand]
    public void CreateShortcut()
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                NotificationService.Instance.Warning("Shortcut creation is only supported on Windows.");
                return;
            }

            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string shortcutPath = Path.Combine(desktopPath, "Nuclear Option (Modded).lnk");

            if (string.IsNullOrEmpty(GamePath) || GamePath == "Not Set")
            {
                NotificationService.Instance.Error("Game path not set.");
                return;
            }

            var shell = Type.GetTypeFromProgID("WScript.Shell");
            if (shell == null) return;

            dynamic wsh = Activator.CreateInstance(shell);
            dynamic shortcut = wsh.CreateShortcut(shortcutPath);

            shortcut.TargetPath = GamePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(GamePath);
            shortcut.Description = "Launch Nuclear Option with mods";
            shortcut.Save();

            NotificationService.Instance.Success("Desktop shortcut created.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create desktop shortcut");
            NotificationService.Instance.Error("Failed to create shortcut.");
        }
    }

    [RelayCommand]
    public void LaunchGame()
    {
        if (string.IsNullOrEmpty(GamePath) || GamePath == "Not Set" || !File.Exists(GamePath))
        {
            NotificationService.Instance.Error("Game not detected. Please set the game path in settings.");
            return;
        }

        try
        {
            string? dir = Path.GetDirectoryName(GamePath);
            if (string.IsNullOrEmpty(dir)) return;

            Process.Start(new ProcessStartInfo
            {
                FileName = GamePath,
                WorkingDirectory = dir,
                UseShellExecute = true
            });

            NotificationService.Instance.Info("Game launched!");

            if (CloseOnLaunch)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var app = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                    app?.MainWindow?.Close();
                }, DispatcherPriority.Background);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to launch game from path: {GamePath}", GamePath);
            NotificationService.Instance.Error("Failed to launch game.");
        }
    }

    //[RelayCommand]
    //public async Task InstallBepInEx()
    //{
    //    if (!IsGameDetected || string.IsNullOrWhiteSpace(SelectedBepInExVersion)) return;

    //    try
    //    {
    //        GameStatus = "Installing BepInEx...";

    //        await _bepInExService.InstallVersionAsync(
    //            SelectedBepInExVersion, 
    //            GamePath, 
    //            p => BepInExDownloadProgress = p);

    //        await RefreshUI();
    //        NotificationService.Instance.Success("BepInEx installed successfully.");
    //    }
    //    catch (Exception ex)
    //    {
    //        Log.Error(ex, "BepInEx installation failed");
    //        NotificationService.Instance.Error("Installation failed.");
    //    }
    //    finally
    //    {
    //        BepInExDownloadProgress = 0;
    //        GameStatus = IsGameDetected ? "Ready" : "Awaiting game path...";
    //    }
    //}

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
                _bepInExService.Uninstall(GamePath);

                await RefreshUI();
                NotificationService.Instance.Success("BepInEx uninstalled.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to uninstall BepInEx");
                NotificationService.Instance.Error("Uninstall failed.");
            }
        }
    }
}