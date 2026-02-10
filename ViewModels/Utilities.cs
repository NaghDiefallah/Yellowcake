using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Yellowcake.Models;
using Yellowcake.Services;

namespace Yellowcake.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private void OpenGameFolder()
    {
        try
        {
            if (string.IsNullOrEmpty(GamePath) || GamePath == "Not Set")
            {
                NotificationService.Instance.Warning("Game path not set");
                return;
            }

            var gameDir = Path.GetDirectoryName(GamePath);
            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
            {
                NotificationService.Instance.Error("Game folder not found");
                return;
            }

            OpenFolderInExplorer(gameDir);
            Log.Information("Opened game folder: {Path}", gameDir);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open game folder");
            NotificationService.Instance.Error("Failed to open folder");
        }
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        try
        {
            var logsPath = Path.Combine(AppContext.BaseDirectory, "logs");
            
            if (!Directory.Exists(logsPath))
            {
                Directory.CreateDirectory(logsPath);
            }

            OpenFolderInExplorer(logsPath);
            Log.Information("Opened logs folder: {Path}", logsPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open logs folder");
            NotificationService.Instance.Error("Failed to open logs folder");
        }
    }

    [RelayCommand]
    private async Task CheckForAppUpdates()
    {
        try
        {
            NotificationService.Instance.Info("Checking for updates...");
            await CheckForAppUpdatesAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to check for updates");
            NotificationService.Instance.Error("Failed to check for updates");
        }
    }

    [RelayCommand]
    private void ClearAppCache()
    {
        try
        {
            var cachePath = Path.Combine(AppContext.BaseDirectory, "cache");
            
            if (Directory.Exists(cachePath))
            {
                Directory.Delete(cachePath, true);
                Directory.CreateDirectory(cachePath);
                NotificationService.Instance.Success("Cache cleared successfully");
                Log.Information("Application cache cleared");
            }
            else
            {
                NotificationService.Instance.Info("No cache to clear");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to clear cache");
            NotificationService.Instance.Error("Failed to clear cache");
        }
    }

    [RelayCommand]
    private async Task RepairInstallation()
    {
        try
        {
            var confirmed = await NotificationService.Instance.ConfirmAsync(
                "Repair Installation",
                "This will verify all installed mods and fix any issues. Continue?");

            if (!confirmed) return;

            NotificationService.Instance.Info("Repairing installation...");

            int repaired = 0;
            foreach (var mod in _installedMods)
            {
                try
                {
                    var isValid = await VerifyInstallationAsync(mod, _shutdownCts.Token);
                    if (!isValid)
                    {
                        // Re-download corrupted mod
                        var remoteMod = _availableMods.FirstOrDefault(m => m.Id == mod.Id);
                        if (remoteMod != null)
                        {
                            await DownloadMod(remoteMod);
                            repaired++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to verify mod: {Mod}", mod.Name);
                }
            }

            NotificationService.Instance.Success($"Repair complete. Fixed {repaired} mod(s)");
            await RefreshUI();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Repair failed");
            NotificationService.Instance.Error("Repair failed");
        }
    }

    [RelayCommand]
    private void OpenDetails(Mod? mod)
    {
        if (mod == null) return;
        
        DetailsMod = mod;
        IsDetailsOpen = true;
    }

    private static void OpenFolderInExplorer(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start("explorer.exe", path);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", path);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", path);
            }
            else
            {
                // Fallback
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open folder in explorer");
            throw;
        }
    }
}