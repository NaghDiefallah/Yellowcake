using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Yellowcake.Models;
using Yellowcake.Services;

namespace Yellowcake.ViewModels;

public partial class MainViewModel
{
    private readonly Dictionary<string, CancellationTokenSource> _activeDownloads = new();

    [RelayCommand]
    public async Task SelectExe()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow is null)
            return;

        var options = new FilePickerOpenOptions
        {
            Title = "Select Nuclear Option Executable",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Nuclear Option Executable")
                {
                    Patterns = new[] { "NuclearOption.exe" }
                }
            }
        };

        try
        {
            var results = await desktop.MainWindow.StorageProvider.OpenFilePickerAsync(options);
            var file = results.FirstOrDefault();

            if (file?.Path.IsAbsoluteUri == true)
            {
                var path = file.Path.LocalPath;
                GamePath = path;
                _modService.SaveGamePath(path);
                _modService.SetGamePath(path);

                Log.Information("Game path updated to: {Path}", path);
                await RefreshUI();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error occurred during executable selection");
            NotificationService.Instance.Error("Could not set game path.");
        }
    }

    [RelayCommand]
    public async Task DownloadMod(Mod? mod)
    {
        if (mod == null || mod.IsDownloading) return;

        if (ModList.Any(m => m.Id == mod.Id))
        {
            Log.Warning("Mod {ModName} is already installed", mod.Name);
            NotificationService.Instance.Warning($"{mod.Name} is already installed.");
            return;
        }

        var cts = new CancellationTokenSource();
        _activeDownloads[mod.Id] = cts;

        try
        {
            mod.IsDownloading = true;
            GameStatus = $"INSTALLING: {mod.Name.ToUpperInvariant()}";

            await _modService.DownloadAndInstallMod(mod, ModList, _allRemoteMods, cts.Token);

            bool verified = await VerifyInstallationAsync(mod, cts.Token);

            if (!cts.Token.IsCancellationRequested && !verified)
            {
                throw new IOException($"Verification failed: Installation directory for {mod.Id} is missing or empty.");
            }

            await RefreshUI();
            GameStatus = "INSTALLATION COMPLETE";
            NotificationService.Instance.Success($"{mod.Name} installed successfully.");
        }
        catch (OperationCanceledException)
        {
            Log.Information("Installation of {ModId} cancelled", mod.Id);
            GameStatus = "CANCELLED";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Installation error for {ModId}", mod.Id);
            GameStatus = "ERROR: CHECK LOGS";
            await MessageBoxManager.GetMessageBoxStandard("Installation Failed",
                $"An error occurred while installing {mod.Name}:\n{ex.Message}",
                ButtonEnum.Ok, Icon.Error).ShowAsync();
        }
        finally
        {
            mod.IsDownloading = false;
            _activeDownloads.Remove(mod.Id);
            cts.Dispose();
        }
    }

    private async Task<bool> VerifyInstallationAsync(Mod mod, CancellationToken ct)
    {
        var gameRoot = _modService.LoadGamePath();
        if (string.IsNullOrEmpty(gameRoot)) return false;

        string gameDir = File.Exists(gameRoot) ? Path.GetDirectoryName(gameRoot)! : gameRoot;

        string path = mod.Category switch
        {
            "Voice Pack" or "Audio" => Path.Combine(gameDir, "BepInEx", "plugins", "WSOYappinator", "audio", mod.Id),
            "Livery" or "Visual" => Path.Combine(gameDir, "NuclearOption_Data", "StreamingAssets", "Liveries", mod.Id),
            _ => Path.Combine(_installService.ModsPath, mod.Id)
        };

        for (int i = 0; i < 10; i++)
        {
            if (ct.IsCancellationRequested) return false;
            if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any()) return true;
            await Task.Delay(1000, ct);
        }
        return false;
    }

    [RelayCommand]
    public void CancelDownload(Mod? mod)
    {
        if (mod != null && _activeDownloads.TryGetValue(mod.Id, out var cts))
        {
            cts.Cancel();
        }
    }

    [RelayCommand]
    public void ToggleMod(Mod? mod)
    {
        if (mod == null || !IsGameDetected) return;

        try
        {
            _modService.ToggleMod(mod.Id, mod.IsEnabled);
            GameStatus = mod.IsEnabled ? $"{mod.Name.ToUpperInvariant()} ARMED" : $"{mod.Name.ToUpperInvariant()} DISARMED";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Toggle failed for {ModId}", mod.Id);
            mod.IsEnabled = !mod.IsEnabled;
            NotificationService.Instance.Error($"Failed to toggle {mod.Name}.");
        }
    }

    [RelayCommand]
    public async Task DeleteMod(Mod? mod)
    {
        if (mod == null) return;

        var result = await MessageBoxManager.GetMessageBoxStandard(
            "Confirm Removal",
            $"Decommission {mod.Name} and remove all associated files?",
            ButtonEnum.YesNo, Icon.Warning).ShowAsync();

        if (result == ButtonResult.Yes)
        {
            try
            {
                _modService.DeleteMod(mod.Id);
                await RefreshUI();
                GameStatus = "MOD REMOVED";
                NotificationService.Instance.Info($"{mod.Name} has been removed.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Delete failed for {ModId}", mod.Id);
                NotificationService.Instance.Error($"Failed to delete {mod.Name}.");
            }
        }
    }

    [RelayCommand]
    public void LaunchGame()
    {
        if (!IsGameDetected || !File.Exists(GamePath)) return;

        try
        {
            Process.Start(new ProcessStartInfo(GamePath)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(GamePath)
            });
            GameStatus = "MISSION IN PROGRESS";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to launch game");
            NotificationService.Instance.Error("Failed to launch Nuclear Option.");
        }
    }

    [RelayCommand]
    public async Task InstallBepInEx()
    {
        if (!IsGameDetected) return;

        try
        {
            GameStatus = "INITIALIZING BEPINEX...";
            string gameDir = Path.GetDirectoryName(GamePath)!;
            var progress = new Progress<double>(p => BepInExDownloadProgress = p);

            await _modService.InstallBepInExAsync(gameDir, progress);
            await RefreshUI();
            GameStatus = "SYSTEMS NOMINAL";
            NotificationService.Instance.Success("BepInEx deployed successfully.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "BepInEx deployment failed");
            GameStatus = "DEPLOYMENT ERROR";
            NotificationService.Instance.Error("BepInEx installation failed.");
        }
        finally
        {
            BepInExDownloadProgress = 0;
        }
    }
}