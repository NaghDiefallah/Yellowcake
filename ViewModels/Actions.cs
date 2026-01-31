using CommunityToolkit.Mvvm.Input;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Serilog;
using System;
using System.Collections.Generic;
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
    public async Task DownloadMod(Mod? mod)
    {
        if (mod == null || mod.IsDownloading) return;

        // 1. Pre-check: Verify if an update is actually needed
        var existing = InstalledMods.FirstOrDefault(m => m.Id == mod.Id);
        if (existing != null && !existing.HasUpdate)
        {
            Log.Debug("Installation skipped: {ModId} is already current.", mod.Id);
            NotificationService.Instance.Warning($"{mod.Name} is already up to date.");
            mod.IsInstalled = true;
            return;
        }

        // 2. Setup Cancellation and Tracking
        using var cts = new CancellationTokenSource();
        if (!_activeDownloads.TryAdd(mod.Id, cts))
        {
            Log.Warning("Download already in progress for {ModId}.", mod.Id);
            return;
        }

        try
        {
            mod.IsDownloading = true;
            mod.DownloadProgress = 0;
            GameStatus = $"Downloading: {mod.Name}";

            // 3. Logic Bypass: Ignore hash verification for voicepacks
            bool skipHash = mod.Tags?.Contains("voicepack", StringComparer.OrdinalIgnoreCase) ?? false;
            string? effectiveHash = skipHash ? null : mod.ExpectedHash;

            if (skipHash) Log.Information("Bypassing hash verification for voicepack: {ModName}", mod.Name);

            // 4. Execution
            var progress = new Progress<double>(p => mod.DownloadProgress = p);
            await _modService.DownloadAndInstallMod(mod, progress, _allRemoteMods, cts.Token, effectiveHash);

            // 5. Post-Download Verification
            if (cts.Token.IsCancellationRequested) return;

            if (await VerifyInstallationAsync(mod, cts.Token))
            {
                Log.Information("Installation verified for {ModName} ({Version})", mod.Name, mod.Version);

                mod.IsInstalled = true;
                mod.IsEnabled = true;
                mod.HasUpdate = false;
                mod.LatestVersion = mod.Version;

                _db.Upsert("addons", mod);

                await SyncInstalledStates();
                await RefreshUI();

                NotificationService.Instance.Success($"{mod.Name} installed successfully.");
            }
            else
            {
                throw new IOException($"Post-install verification failed for {mod.Name}. Expected files not found.");
            }
        }
        catch (OperationCanceledException)
        {
            Log.Information("Installation cancelled by user: {ModId}", mod.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fault during mod installation: {ModId}", mod.Id);
            GameStatus = "Error";

            await MessageBoxManager.GetMessageBoxStandard(
                "Installation Failed",
                $"{mod.Name} could not be installed.\n\nError: {ex.Message}",
                ButtonEnum.Ok,
                Icon.Error).ShowAsync();
        }
        finally
        {
            // 6. Cleanup
            mod.IsDownloading = false;
            mod.DownloadProgress = 0;
            _activeDownloads.Remove(mod.Id);

            if (GameStatus != "Error")
            {
                GameStatus = "Ready";
            }
        }
    }

    [RelayCommand]
    public void CancelDownload(Mod? mod)
    {
        if (mod != null && _activeDownloads.TryGetValue(mod.Id, out var cts))
        {
            cts.Cancel();
            GameStatus = "Cancelling download...";
        }
    }

    [RelayCommand]
    public void ToggleMod(Mod? mod)
    {
        if (mod == null || !IsGameDetected || !mod.IsInstalled) return;

        try
        {
            _modService.ToggleMod(mod.Id, mod.IsEnabled);
            _db.Upsert("addons", mod);
            GameStatus = mod.IsEnabled ? $"{mod.Name} enabled" : $"{mod.Name} disabled";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Toggle failed: {ModId}", mod.Id);
            mod.IsEnabled = !mod.IsEnabled;
            NotificationService.Instance.Error("Failed to toggle mod. Check folder permissions.");
        }
    }

    [RelayCommand]
    public async Task DeleteMod(Mod? mod)
    {
        if (mod == null || !mod.IsInstalled) return;

        var result = await MessageBoxManager.GetMessageBoxStandard("Confirm Uninstallation",
            $"Are you sure you want to remove {mod.Name}?",
            ButtonEnum.YesNo, Icon.Warning).ShowAsync();

        if (result != ButtonResult.Yes) return;

        try
        {
            GameStatus = $"Deleting {mod.Name}...";
            _modService.DeleteMod(mod.Id);
            _db.Delete("addons", mod.Id);

            mod.IsInstalled = false;
            mod.IsEnabled = false;
            mod.HasUpdate = false;

            await SyncInstalledStates();
            await RefreshUI();

            NotificationService.Instance.Info($"{mod.Name} removed.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Delete failed for {ModId}", mod.Id);
            NotificationService.Instance.Error("Could not delete files. Is the game running?");
        }
        finally
        {
            GameStatus = "Ready";
        }
    }

    [RelayCommand]
    public async Task BulkUpdate()
    {
        var updates = InstalledMods.Where(m => m.HasUpdate).ToList();
        if (!updates.Any())
        {
            NotificationService.Instance.Info("All mods are up to date.");
            return;
        }

        foreach (var mod in updates)
        {
            await DownloadMod(mod);
        }
    }

    [RelayCommand]
    public void EnableAll()
    {
        foreach (var mod in InstalledMods.Where(m => !m.IsEnabled))
        {
            mod.IsEnabled = true;
            ToggleMod(mod);
        }
    }

    [RelayCommand]
    public void DisableAll()
    {
        foreach (var mod in InstalledMods.Where(m => m.IsEnabled))
        {
            mod.IsEnabled = false;
            ToggleMod(mod);
        }
    }

    [RelayCommand]
    public async Task DeleteAll()
    {
        var installed = InstalledMods.ToList();
        if (!installed.Any()) return;

        var result = await MessageBoxManager.GetMessageBoxStandard("Confirm Mass Delete",
            $"Are you sure you want to delete ALL {installed.Count} installed mods?",
            ButtonEnum.YesNo, Icon.Stop).ShowAsync();

        if (result != ButtonResult.Yes) return;

        foreach (var mod in installed)
        {
            try
            {
                _modService.DeleteMod(mod.Id);
                _db.Delete("addons", mod.Id);
                mod.IsInstalled = false;
                mod.IsEnabled = false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Mass delete failed for {Id}", mod.Id);
            }
        }

        await SyncInstalledStates();
        await RefreshUI();
        GameStatus = "Ready";
        NotificationService.Instance.Success("All mods removed.");
    }
}