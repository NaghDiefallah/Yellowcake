using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private CancellationTokenSource? _searchDebounceTokenSource;

    [ObservableProperty] private ModFilter _activeFilter = ModFilter.All;
    [ObservableProperty] private string? _searchText;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _currentSort = "NameAsc";

    partial void OnActiveFilterChanged(ModFilter value) => _ = ApplyFilters();

    partial void OnSearchTextChanged(string? value)
    {
        _searchDebounceTokenSource?.Cancel();
        _searchDebounceTokenSource = new CancellationTokenSource();
        var token = _searchDebounceTokenSource.Token;

        Task.Delay(300, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
                Dispatcher.UIThread.Post(() => ApplyFiltersCommand.Execute(null));
        }, TaskScheduler.Default);
    }

    [RelayCommand]
    private async Task SetSort(string sortMode)
    {
        if (CurrentSort == sortMode) return;
        CurrentSort = sortMode;
        await ApplyFilters();
    }

    [RelayCommand]
    private async Task SetFilter(ModFilter filter)
    {
        if (ActiveFilter == filter) return;
        ActiveFilter = filter;
        await ApplyFilters();
    }

    [RelayCommand]
    private async Task ApplyFilters()
    {
        if (_allRemoteMods == null) return;

        var search = SearchText?.Trim();
        var currentFilter = ActiveFilter;
        var sortMode = CurrentSort;

        var result = await Task.Run(() =>
        {
            var query = _allRemoteMods.AsEnumerable();

            query = currentFilter switch
            {
                ModFilter.Voice => query.Where(m => m.IsVoicePack),
                ModFilter.Livery => query.Where(m => m.IsLivery),
                ModFilter.Missions => query.Where(m => m.IsMission),
                ModFilter.Plugins => query.Where(m => !m.IsLivery && !m.IsVoicePack && !m.IsMission),
                ModFilter.Installed => query.Where(m => m.IsInstalled),
                _ => query
            };

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(m =>
                    (m.Name?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (m.Author?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            query = sortMode switch
            {
                "NameDesc" => query.OrderByDescending(m => m.Name),
                "Author" => query.OrderBy(m => m.Author),
                "Date" => query.OrderByDescending(m => m.Id),
                _ => query.OrderBy(m => m.Name)
            };

            return query.ToList();
        });

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (AvailableMods.SequenceEqual(result)) return;

            AvailableMods.Clear();
            foreach (var mod in result) AvailableMods.Add(mod);
        });
    }

    public async Task RefreshUI()
    {
        var installed = _modService.GetInstalledMods();
        var gameDetected = !string.IsNullOrEmpty(GamePath) && GamePath != "Not Set";
        var bepInstalled = gameDetected && _modService.IsBepInExInstalled();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!InstalledMods.SequenceEqual(installed))
            {
                InstalledMods.Clear();
                foreach (var mod in installed) InstalledMods.Add(mod);
            }

            IsGameDetected = gameDetected;
            IsBepInstalled = bepInstalled;
            UpdateGameStatus(gameDetected, bepInstalled);
        });
    }

    private void UpdateGameStatus(bool detected, bool modded)
    {
        GameStatus = !detected ? "Awaiting game path..." :
                     !modded ? "BepInEx required" :
                     "Ready to play";
    }

    private async Task SyncInstalledStates()
    {
        var localMods = _modService.GetInstalledMods().ToDictionary(m => m.Id);

        foreach (var remote in _allRemoteMods)
        {
            if (localMods.TryGetValue(remote.Id, out var local))
            {
                remote.IsInstalled = true;
                remote.IsEnabled = local.IsEnabled;
                remote.HasUpdate = _modService.IsUpdateAvailable(local, remote);
                local.LatestVersion = remote.Version;
                _db.Upsert("addons", local);
            }
            else
            {
                remote.IsInstalled = false;
                remote.HasUpdate = false;
            }
        }

        await ApplyFilters();
    }

    [RelayCommand]
    private async Task RefreshModsAsync()
    {
        GameStatus = "Refreshing mod list...";
        IsLoading = true;
        try
        {
            await LoadAvailableModsAsync();
            NotificationService.Instance.Success("Mod list refreshed.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Refresh failed");
            NotificationService.Instance.Error("Failed to refresh mods.");
        }
        finally
        {
            IsLoading = false;
            UpdateGameStatus(IsGameDetected, IsBepInstalled);
        }
    }

    private async Task LoadAvailableModsAsync()
    {
        try
        {
            var mods = await _modService.FetchRemoteManifest();
            _allRemoteMods = mods ?? new List<Mod>();
            await SyncInstalledStates();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Manifest sync failed.");
            await Dispatcher.UIThread.InvokeAsync(() => GameStatus = "Sync Error");
            throw;
        }
    }

    private async Task<bool> VerifyInstallationAsync(Mod mod, CancellationToken token)
    {
        if (mod == null)
        {
            Log.Warning("Verification aborted: Mod object is null.");
            return false;
        }

        return await Task.Run(() =>
        {
            try
            {
                Log.Debug("Starting lightning-verify for {ModName} ({ModId})", mod.Name, mod.Id);

                string targetPath;
                bool isVoicePack = mod.Tags?.Any(t => t.Equals("voicepack", StringComparison.OrdinalIgnoreCase)) ?? false;

                if (isVoicePack)
                {
                    string gameDir = Path.GetDirectoryName(GamePath) ?? string.Empty;
                    string audioDir = Path.Combine(gameDir, "BepInEx", "plugins", "WSOYappinator", "audio");

                    if (!Directory.Exists(audioDir))
                    {
                        Log.Information("Creating missing voicepack directory: {Path}", audioDir);
                        Directory.CreateDirectory(audioDir);
                    }

                    targetPath = Path.Combine(audioDir, mod.Id);
                }
                else
                {
                    targetPath = Path.Combine(_installService.ModsPath, mod.Id);
                }

                if (token.IsCancellationRequested)
                {
                    Log.Information("Verification cancelled for {ModName}", mod.Name);
                    return false;
                }

                // Using Path.Exists is faster as it performs a single system call for either file or directory
                bool exists = Path.Exists(targetPath);

                if (exists)
                {
                    Log.Information("Verification successful: {ModName} found at {Path}", mod.Name, targetPath);
                }
                else
                {
                    Log.Warning("Verification failed: {ModName} is missing from expected location {Path}", mod.Name, targetPath);
                }

                return exists;
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Error(ex, "Permission denied while accessing path for {ModId}", mod.Id);
                return false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Critical error during fast-verification of {ModId}", mod.Id);
                return false;
            }
        }, token);
    }

    private bool GetBoolSetting(string key, bool defaultValue)
    {
        var value = _db.GetSetting(key);
        return value == null ? defaultValue : bool.TryParse(value, out var result) && result;
    }
}