using Avalonia.Collections;
using CommunityToolkit.Mvvm.Input;
using System.Linq;
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

        foreach (var mod in toInstall)
        {
            await DownloadMod(mod);
        }

        DeselectAllMods();
    }

    [RelayCommand]
    private async Task BatchUninstall()
    {
        var toUninstall = _selectedMods.Where(m => m.IsInstalled).ToList();
        if (!toUninstall.Any()) return;

        bool confirmed = await NotificationService.Instance.ConfirmAsync(
            "Batch Uninstall",
            $"Remove {toUninstall.Count} mod(s)?");

        if (!confirmed) return;

        foreach (var mod in toUninstall)
        {
            await DeleteMod(mod);
        }

        DeselectAllMods();
    }

    [RelayCommand]
    private void BatchEnable()
    {
        foreach (var mod in _selectedMods.Where(m => m.IsInstalled && !m.IsEnabled))
        {
            mod.IsEnabled = true;
            ToggleMod(mod);
        }

        DeselectAllMods();
    }

    [RelayCommand]
    private void BatchDisable()
    {
        foreach (var mod in _selectedMods.Where(m => m.IsInstalled && m.IsEnabled))
        {
            mod.IsEnabled = false;
            ToggleMod(mod);
        }

        DeselectAllMods();
    }
}