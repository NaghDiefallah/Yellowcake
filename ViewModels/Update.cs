using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;
using Yellowcake.Models;

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
            GameStatus = $"UPDATING: {mod.Name?.ToUpperInvariant() ?? mod.Id.ToUpperInvariant()}";

            var remoteMod = _allRemoteMods.FirstOrDefault(m => m.Id == mod.Id);
            if (remoteMod == null)
            {
                Log.Warning("Update target {ModId} not found in remote manifest.", mod.Id);
                GameStatus = "MOD NOT IN MANIFEST";
                return;
            }

            await _modService.DownloadAndInstallMod(remoteMod, ModList, _allRemoteMods);

            mod.HasUpdate = false;
            if (!string.IsNullOrEmpty(mod.LatestVersion))
            {
                mod.Version = mod.LatestVersion;
            }

            Log.Information("Successfully updated {ModName} to version {Version}", mod.Name, mod.Version);

            await RefreshUI();
            GameStatus = "READY FOR SORTIE";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Update failed for {ModName}", mod.Name);
            GameStatus = "UPDATE ERROR";
        }
        finally
        {
            mod.IsDownloading = false;
        }
    }

    [RelayCommand]
    public async Task CheckForUpdates()
    {
        GameStatus = "CHECKING FOR UPDATES...";

        try
        {
            await LoadAvailableModsAsync();

            bool updatesFound = ModList.Any(m => m.HasUpdate);
            GameStatus = updatesFound ? "UPDATES AVAILABLE" : "ALL MODS UP TO DATE";

            Log.Information("Manual update check complete. Updates found: {UpdatesFound}", updatesFound);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Manual update check failed");
            GameStatus = "SYNC ERROR";
        }
    }
}