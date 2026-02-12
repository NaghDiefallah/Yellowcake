using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Octokit;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Yellowcake.Models;
using Yellowcake.Services;

namespace Yellowcake.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private bool _isDownloading;
    private bool _isCheckingUpdates;

    [RelayCommand]
    private void CancelBusy()
    {
        IsBusy = false;
        // Logic to stop active tasks can be added here
    }

    [RelayCommand]
    public async Task UpdateMod(Mod? mod)
    {
        if (mod == null || mod.IsDownloading) return;

        try
        {
            mod.IsDownloading = true;
            mod.DownloadProgress = 0;
            GameStatus = $"Updating: {mod.Name}";

            var remoteMod = _allRemoteMods.FirstOrDefault(m => m.Id == mod.Id);
            if (remoteMod == null)
            {
                Log.Warning("Update target {ModId} missing from manifest", mod.Id);
                GameStatus = "Mod not in manifest";
                return;
            }

            // Use the correct async API and provide a CancellationToken
            await _modService.DownloadAndInstallModAsync(remoteMod, null, _allRemoteMods, CancellationToken.None);

            mod.HasUpdate = false;
            mod.InstalledVersionString = remoteMod.Version;

            _db.Upsert("addons", mod);

            Log.Information("Updated {ModName} to {Version}", mod.Name, mod.Version);

            await RefreshUI();
            GameStatus = "Ready";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Update failed for {ModName}", mod.Name);
            GameStatus = "Update Error";
            NotificationService.Instance.Error($"Failed to update {mod.Name}");
        }
        finally
        {
            mod.IsDownloading = false;
            mod.DownloadProgress = 0;
        }
    }

    [RelayCommand]
    public async Task UpdateAllMods()
    {
        var updates = InstalledMods.Where(m => m.HasUpdate).ToList();
        if (updates.Count == 0) return;

        int successCount = 0;
        foreach (var mod in updates)
        {
            try
            {
                await UpdateMod(mod);
                successCount++;
            }
            catch
            {
                continue;
            }
        }

        NotificationService.Instance.Success($"Successfully updated {successCount} mods.");
        await RefreshUI();
    }

    [RelayCommand]
    public async Task CheckForUpdates()
    {
        GameStatus = "Checking for updates...";
        try
        {
            await LoadAvailableModsAsync();

            bool hasUpdates = InstalledMods.Any(m => m.HasUpdate);
            GameStatus = hasUpdates ? "Updates Available" : "All mods up to date";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Manual update check failed");
            GameStatus = "Sync Error";
        }
    }

    private async Task DownloadAndApplyUpdate(Release release)
    {
        var asset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (asset == null) return;

        string downloadPath = Path.Combine(Path.GetTempPath(), asset.Name);

        try
        {
            IsBusy = true;
            CanCancel = false;
            BusyMessage = "Preparing download...";

            using (var client = new HttpClient())
            using (var response = await client.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(downloadPath, System.IO.FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[8192];
                    long totalRead = 0;
                    int read;

                    while ((read = await contentStream.ReadAsync(buffer)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, read));
                        totalRead += read;

                        if (totalBytes != -1)
                        {
                            var progress = (double)totalRead / totalBytes * 100;
                            BusyMessage = $"Downloading Update: {progress:F0}%";
                        }
                    }
                }
            }

            await ExecuteUpdateTransition(downloadPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Update download failed.");
            NotificationService.Instance.Error("Failed to download update.");
            IsBusy = false;
        }
    }

    private async Task ExecuteUpdateTransition(string downloadPath)
    {
        try
        {
            BusyMessage = "Installing update...";

            string currentExe = Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("Could not resolve current executable path.");

            string scriptPath = Path.Combine(Path.GetTempPath(), "update_yellowcake.bat");

            string batchContent = $@"
@echo off
timeout /t 1 /nobreak > nul
:loop
del ""{currentExe}"" > nul 2>&1
if exist ""{currentExe}"" goto loop
move /y ""{downloadPath}"" ""{currentExe}""
start """" ""{currentExe}""
del ""%~f0""";

            await File.WriteAllTextAsync(scriptPath, batchContent);

            Process.Start(new ProcessStartInfo
            {
                FileName = scriptPath,
                CreateNoWindow = true,
                UseShellExecute = true
            });

            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Transition to new version failed.");
            throw;
        }
    }

    private async Task CheckForAppUpdatesAsync()
    {
        if (_isCheckingUpdates) return;
        _isCheckingUpdates = true;

        try
        {
            var latestRelease = await _gh.Repository.Release.GetLatest("NaghDiefallah", "Yellowcake");

            string remoteTag = latestRelease.TagName.Trim().TrimStart('v', 'V');

            if (!Version.TryParse(remoteTag, out var latest) || !Version.TryParse(CurrentVersion, out var current))
            {
                return;
            }

            if (latest <= current) return;

            if (latest > current)
            {
                var result = await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var box = MessageBoxManager.GetMessageBoxStandard(
                        "Update Available",
                        $"A new version (v{latest}) is available! Would you like to download and install it now?",
                        ButtonEnum.YesNo,
                        Icon.Info);

                    return await box.ShowAsync();
                });

                if (result == ButtonResult.Yes)
                {
                    await DownloadAndApplyUpdate(latestRelease);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update check failed.");
        }
        finally
        {
            _isCheckingUpdates = false;

            if (!IsBusy || BusyMessage.Contains("Checking"))
            {
                IsBusy = false;
            }
        }
    }

    private async Task CheckForModUpdatesAsync()
    {
        if (!_installedMods.Any())
        {
            Log.Debug("No installed mods to check for updates");
            return;
        }

        try
        {
            Log.Information("Checking for mod updates...");
            
            int updateCount = 0;

            foreach (var installed in _installedMods)
            {
                var remote = _allRemoteMods.FirstOrDefault(m => 
                    string.Equals(m.Id, installed.Id, StringComparison.OrdinalIgnoreCase));

                if (remote != null && ModService.HasUpdate(installed, remote))
                {
                    installed.HasUpdate = true;
                    installed.LatestVersion = remote.Version;
                    updateCount++;
                    
                    Log.Information("Update available: {ModName} {OldVer} -> {NewVer}", 
                        installed.Name, installed.Version, remote.Version);
                }
                else
                {
                    installed.HasUpdate = false;
                    installed.LatestVersion = installed.Version;
                }
            }

            if (updateCount > 0)
            {
                NotificationService.Instance.Info($"{updateCount} mod update(s) available!");
            }
            else
            {
                Log.Information("All mods are up to date");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to check for mod updates");
        }
    }
}