using Avalonia.Collections;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Yellowcake.Models;
using Yellowcake.Services;

namespace Yellowcake.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private void ToggleBatchMode()
    {
        IsBatchMode = !IsBatchMode;
        if (!IsBatchMode)
        {
            DeselectAllMods();
        }
    }

    [RelayCommand]
    private void ToggleModSelection(Mod? mod)
    {
        if (mod == null || !IsBatchMode) return;

        if (_selectedMods.Contains(mod))
        {
            _selectedMods.Remove(mod);
            mod.IsSelectedInBatch = false;
        }
        else
        {
            _selectedMods.Add(mod);
            mod.IsSelectedInBatch = true;
        }
    }

    [RelayCommand]
    private void DeselectAllMods()
    {
        foreach (var mod in _selectedMods)
        {
            mod.IsSelectedInBatch = false;
        }
        _selectedMods.Clear();
    }

    [RelayCommand]
    private async Task BatchInstall()
    {
        var toInstall = _selectedMods.Where(m => !m.IsInstalled).ToList();
        if (!toInstall.Any()) return;

        int successCount = 0;
        int failCount = 0;
        var errors = new List<(string ModId, string Error)>();

        var tasks = toInstall.Select(async mod =>
        {
            try
            {
                await DownloadMod(mod);
                Interlocked.Increment(ref successCount);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Batch install failed for {ModId}", mod.Id);
                lock (errors)
                {
                    errors.Add((mod.Id, ex.Message));
                }
                Interlocked.Increment(ref failCount);
            }
        });
        await Task.WhenAll(tasks);

        DeselectAllMods();

        if (failCount > 0)
        {
            NotificationService.Instance.Warning(
                $"Installed {successCount} mod(s), {failCount} failed. Check logs for details.");
        }
        else if (successCount > 0)
        {
            NotificationService.Instance.Success($"Installed {successCount} mod(s).");
        }
    }

    [RelayCommand]
    private async Task BatchUninstall()
    {
        var toUninstall = _selectedMods.Where(m => m.IsInstalled).ToList();
        if (!toUninstall.Any()) return;

        bool confirmed = await NotificationService.Instance.ConfirmAsync(
            "Batch Uninstall",
            $"Remove {toUninstall.Count} mod(s)? This cannot be undone.");

        if (!confirmed) return;

        int successCount = 0;
        int failCount = 0;

        var tasks = toUninstall.Select(async mod =>
        {
            try
            {
                await DeleteMod(mod);
                Interlocked.Increment(ref successCount);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Batch uninstall failed for {ModId}", mod.Id);
                Interlocked.Increment(ref failCount);
            }
        });
        await Task.WhenAll(tasks);

        DeselectAllMods();

        if (failCount > 0)
        {
            NotificationService.Instance.Warning(
                $"Removed {successCount} mod(s), {failCount} failed. Check logs for details.");
        }
        else
        {
            NotificationService.Instance.Success($"Removed {successCount} mod(s).");
        }
    }

    [RelayCommand]
    private async Task BatchEnable()
    {
        var toEnable = _selectedMods
            .Where(m => m.IsInstalled && !m.IsEnabled)
            .ToList();
        if (!toEnable.Any()) return;

        int successCount = 0;
        int failCount = 0;

        var tasks = toEnable.Select(async mod =>
        {
            mod.IsEnabled = true;
            try
            {
                await ToggleMod(mod);
                Interlocked.Increment(ref successCount);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Batch enable failed for {ModId}", mod.Id);
                mod.IsEnabled = false;
                Interlocked.Increment(ref failCount);
            }
        });
        await Task.WhenAll(tasks);

        DeselectAllMods();

        if (failCount > 0)
        {
            NotificationService.Instance.Warning(
                $"Enabled {successCount} mod(s), {failCount} failed.");
        }
        else if (successCount > 0)
        {
            NotificationService.Instance.Success($"Enabled {successCount} mod(s).");
        }
    }

    [RelayCommand]
    private async Task BatchDisable()
    {
        var toDisable = _selectedMods
            .Where(m => m.IsInstalled && m.IsEnabled)
            .ToList();
        if (!toDisable.Any()) return;

        int successCount = 0;
        int failCount = 0;

        var tasks = toDisable.Select(async mod =>
        {
            mod.IsEnabled = false;
            try
            {
                await ToggleMod(mod);
                Interlocked.Increment(ref successCount);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Batch disable failed for {ModId}", mod.Id);
                mod.IsEnabled = true;
                Interlocked.Increment(ref failCount);
            }
        });
        await Task.WhenAll(tasks);

        DeselectAllMods();

        if (failCount > 0)
        {
            NotificationService.Instance.Warning(
                $"Disabled {successCount} mod(s), {failCount} failed.");
        }
        else if (successCount > 0)
        {
            NotificationService.Instance.Success($"Disabled {successCount} mod(s).");
        }
    }
}