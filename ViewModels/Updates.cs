using CommunityToolkit.Mvvm.Input;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Serilog;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Yellowcake.Models;
using Yellowcake.Services;

namespace Yellowcake.ViewModels;

public partial class MainViewModel
{
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

            await _modService.DownloadAndInstallMod(remoteMod, null!, _allRemoteMods);

            mod.HasUpdate = false;
            mod.Version = remoteMod.Version;

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

    private async Task CheckForAppUpdatesAsync()
    {
        try
        {
            var latestRelease = await _gh.Repository.Release.GetLatest("NaghDiefallah", "Yellowcake");
            var cleanTag = latestRelease.TagName.TrimStart('v', 'V').Split('-')[0];

            if (Version.TryParse(cleanTag, out var latest) && Version.TryParse(CurrentVersion, out var current))
            {
                if (latest > current)
                {
                    var result = await MessageBoxManager.GetMessageBoxStandard(
                        "App Update",
                        $"Version {latestRelease.TagName} is available. Download now?",
                        ButtonEnum.YesNo,
                        Icon.Info).ShowAsync();

                    if (result == ButtonResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo(latestRelease.HtmlUrl) { UseShellExecute = true });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning("App update check skipped: {Message}", ex.Message);
        }
    }
}