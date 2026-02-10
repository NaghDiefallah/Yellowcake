using Avalonia.Threading;
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
    private CancellationTokenSource? _filterTokenSource;

    [RelayCommand]
    private async Task SetSort(string sortMode)
    {
        if (_currentSort == sortMode) return;
        _currentSort = sortMode;
        await ApplyFilters();
    }

    [RelayCommand]
    private async Task SetFilter(ModFilter filter)
    {
        if (_activeFilter == filter) return;
        _activeFilter = filter;
        await ApplyFilters();
    }

    [RelayCommand]
    private async Task ApplyFilters()
    {
        if (_allRemoteMods == null) return;

        _filterTokenSource?.Cancel();
        _filterTokenSource = new CancellationTokenSource();
        var token = _filterTokenSource.Token;

        var search = _searchText?.Trim();
        var currentFilter = _activeFilter;
        var sortMode = _currentSort;

        _isFiltering = true;
        OnPropertyChanged(nameof(IsFiltering));

        try
        {
            var result = await Task.Run(() =>
            {
                if (token.IsCancellationRequested) return Enumerable.Empty<Mod>();

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

                if (token.IsCancellationRequested) return Enumerable.Empty<Mod>();

                if (!string.IsNullOrWhiteSpace(search))
                {
                    query = query.Where(m =>
                        (m.Name?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (m.Authors?.Any(a => a?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ?? false) ||
                        (m.Tags?.Any(t => t?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ?? false));
                }

                if (token.IsCancellationRequested) return Enumerable.Empty<Mod>();

                query = sortMode switch
                {
                    "NameDesc" => query.OrderByDescending(m => m.Name),
                    "Author" => query.OrderBy(m => m.Authors?.FirstOrDefault() ?? string.Empty),
                    "Date" => query.OrderByDescending(m => m.Id),
                    _ => query.OrderBy(m => m.Name)
                };

                return query.ToList();
            }, token);

            if (token.IsCancellationRequested) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_availableMods.SequenceEqual(result)) return;

                _availableMods.Clear();
                foreach (var mod in result) _availableMods.Add(mod);
            });

            _ = Task.Run(() => FetchMissingFileSizesAsync(result.Take(20).ToList(), token));
        }
        catch (OperationCanceledException)
        {
            Log.Debug("Filter operation cancelled");
        }
        finally
        {
            _isFiltering = false;
            OnPropertyChanged(nameof(IsFiltering));
        }
    }

    private async Task FetchMissingFileSizesAsync(List<Mod> mods, CancellationToken ct)
    {
        foreach (var mod in mods)
        {
            if (ct.IsCancellationRequested) break;
            
            if (mod.FileSizeBytes <= 0)
            {
                try
                {
                    await mod.FetchFileSizeAsync(ct);
                    await Task.Delay(100, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Failed to fetch size for {Mod}", mod.Name);
                }
            }
        }
    }

    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open URL: {Url}", url);
            NotificationService.Instance.Error($"Failed to open link: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        _searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        Dispatcher.UIThread.Post(() => _ = ApplyFilters());
    }

    [RelayCommand]
    private void SearchByTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return;
        _searchText = tag;
        OnPropertyChanged(nameof(SearchText));
        Dispatcher.UIThread.Post(() => _ = ApplyFilters());
    }

    public async Task RefreshUI()
    {
        var installed = _modService.GetInstalledMods();
        var gameDetected = !string.IsNullOrEmpty(_gamePath) && _gamePath != "Not Set";
        var bepInstalled = gameDetected && _modService.IsBepInExInstalled();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!_installedMods.SequenceEqual(installed))
            {
                _installedMods.Clear();
                foreach (var mod in installed) _installedMods.Add(mod);
            }

            _isGameDetected = gameDetected;
            _isBepInstalled = bepInstalled;
            OnPropertyChanged(nameof(IsGameDetected));
            OnPropertyChanged(nameof(IsBepInstalled));
            UpdateGameStatus(gameDetected, bepInstalled);
        });
    }

    private void UpdateGameStatus(bool detected, bool modded)
    {
        _gameStatus = !detected ? "Awaiting game path..." :
                     !modded ? "BepInEx required" :
                     "Ready to play";
        OnPropertyChanged(nameof(GameStatus));
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
                remote.HasUpdate = ModService.HasUpdate(local, remote);
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
        _gameStatus = "Refreshing mod list...";
        OnPropertyChanged(nameof(GameStatus));
        _isLoading = true;
        OnPropertyChanged(nameof(IsLoading));
        
        try
        {
            await LoadAvailableModsAsync();
            NotificationService.Instance.Success("Mod list refreshed successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Refresh failed");
            NotificationService.Instance.Error($"Failed to refresh mods: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
            OnPropertyChanged(nameof(IsLoading));
            UpdateGameStatus(_isGameDetected, _isBepInstalled);
        }
    }

    private async Task LoadAvailableModsAsync()
    {
        try
        {
            _isLoading = true;
            OnPropertyChanged(nameof(IsLoading));

            var mods = await _manifestService.FetchRemoteManifestAsync(_shutdownCts.Token);
            _allRemoteMods = mods;

            foreach (var mod in _allRemoteMods)
            {
                mod.FinalizeFromManifest();
            }

            await SyncInstalledStates();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _availableMods.Clear();
                foreach (var mod in _allRemoteMods)
                {
                    _availableMods.Add(mod);
                }
            });

            Log.Information("Loaded {Count} mods", mods.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load mods from {Url}", _manifestService.TargetUrl);
            _gameStatus = "Sync Error";
            OnPropertyChanged(nameof(GameStatus));
            NotificationService.Instance.Error("Failed to load mod list");
        }
        finally
        {
            _isLoading = false;
            OnPropertyChanged(nameof(IsLoading));
        }
    }

    private async Task<bool> VerifyInstallationAsync(Mod mod, CancellationToken token)
    {
        if (mod == null) return false;

        return await Task.Run(() =>
        {
            try
            {
                string targetPath;
                bool isVoicePack = mod.IsVoicePack;

                if (isVoicePack)
                {
                    string gameDir = Path.GetDirectoryName(_gamePath) ?? string.Empty;
                    string audioDir = Path.Combine(gameDir, "BepInEx", "plugins", "WSOYappinator", "audio");

                    if (!Directory.Exists(audioDir))
                    {
                        Directory.CreateDirectory(audioDir);
                    }

                    targetPath = Path.Combine(audioDir, mod.Id);
                }
                else
                {
                    targetPath = Path.Combine(_installService.ModsPath, mod.Id);
                }

                if (token.IsCancellationRequested) return false;

                return Directory.Exists(targetPath) || File.Exists(targetPath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Verification failed for {ModId}", mod.Id);
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