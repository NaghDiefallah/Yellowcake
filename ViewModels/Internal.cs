using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
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

        var sw = Stopwatch.StartNew();

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
                    ModFilter.External => query.Where(m => m.IsExternalSource),
                    ModFilter.Plugins => query.Where(m => !m.IsLivery && !m.IsVoicePack && !m.IsMission),
                    ModFilter.Installed => query.Where(m => m.IsInstalled),
                    _ => query
                };

                if (token.IsCancellationRequested) return Enumerable.Empty<Mod>();

                if (!string.IsNullOrWhiteSpace(search))
                {
                    query = query.Where(m =>
                    {
                        if (string.IsNullOrEmpty(m.Id)) return false;
                        return _searchIndex.TryGetValue(m.Id, out var indexed) && indexed.Contains(search, StringComparison.OrdinalIgnoreCase);
                    });
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
                ApplyAvailableModsDelta(result);
            });

            _ = Task.Run(() => FetchMissingFileSizesAsync(result.Take(24).ToList(), token));
        }
        catch (OperationCanceledException)
        {
            Log.Debug("Filter operation cancelled");
        }
        finally
        {
            sw.Stop();
            _performanceTracker.RecordOperation("ApplyFilters", sw.Elapsed, true);
            _isFiltering = false;
            OnPropertyChanged(nameof(IsFiltering));
        }
    }

    private async Task FetchMissingFileSizesAsync(List<Mod> mods, CancellationToken ct)
    {
        using var semaphore = new SemaphoreSlim(4, 4);
        var tasks = mods
            .Where(m => m.FileSizeBytes <= 0)
            .Select(async mod =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    await mod.FetchFileSizeAsync(ct);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Failed to fetch size for {Mod}", mod.Name);
                }
                finally
                {
                    semaphore.Release();
                }
            })
            .ToList();

        await Task.WhenAll(tasks);
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
        var sw = Stopwatch.StartNew();
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

        _performanceTracker.RecordOperation("RefreshUI", sw.Elapsed, true);
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
        var reconciliation = _modService.ReconcileInstalledModsWithDisk(_allRemoteMods);

        if (reconciliation.TotalChanges > 0)
        {
            Log.Information(
                "Reconciled installed mods with disk. Discovered: {Discovered}, External: {ExternalDiscovered}, Updated: {Updated}, Removed: {Removed}",
                reconciliation.Discovered,
                reconciliation.ExternalDiscovered,
                reconciliation.Updated,
                reconciliation.Removed);

            var summary = $"{reconciliation.Discovered}:{reconciliation.ExternalDiscovered}:{reconciliation.Updated}:{reconciliation.Removed}";

            var now = DateTime.UtcNow;
            if (summary != _lastReconciliationSummary &&
                (now - _lastReconciliationNoticeUtc).TotalSeconds >= 30)
            {
                NotificationService.Instance.Info(
                    $"Synced external mod changes: +{reconciliation.Discovered} discovered ({reconciliation.ExternalDiscovered} external), ~{reconciliation.Updated} updated, -{reconciliation.Removed} removed.");
                _lastReconciliationNoticeUtc = now;
                _lastReconciliationSummary = summary;
            }
        }

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
        var sw = Stopwatch.StartNew();
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

            RebuildIndexes(_allRemoteMods);
            await SyncInstalledStates();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ApplyAvailableModsDelta(_allRemoteMods);
            });

            Log.Information("Loaded {Count} mods", mods.Count);
            _performanceTracker.RecordOperation("LoadAvailableMods", sw.Elapsed, true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load mods from {Url}", _manifestService.TargetUrl);
            _gameStatus = "Sync Error";
            OnPropertyChanged(nameof(GameStatus));
            NotificationService.Instance.Error("Failed to load mod list");
            _performanceTracker.RecordOperation("LoadAvailableMods", sw.Elapsed, false);
        }
        finally
        {
            _isLoading = false;
            OnPropertyChanged(nameof(IsLoading));
        }
    }

    private void RebuildIndexes(List<Mod> mods)
    {
        _remoteModIndex = mods
            .Where(m => !string.IsNullOrWhiteSpace(m.Id))
            .GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        _searchIndex = mods
            .Where(m => !string.IsNullOrWhiteSpace(m.Id))
            .ToDictionary(
                m => m.Id,
                m => string.Join(' ', new[]
                {
                    m.Name ?? string.Empty,
                    string.Join(' ', m.Authors ?? new List<string>()),
                    string.Join(' ', m.Tags ?? new List<string>())
                }).ToLowerInvariant(),
                StringComparer.OrdinalIgnoreCase);
    }

    private void ApplyAvailableModsDelta(IEnumerable<Mod> incoming)
    {
        var incomingList = incoming.ToList();

        if (_availableMods.Count == incomingList.Count)
        {
            var sameOrder = true;
            for (var i = 0; i < _availableMods.Count; i++)
            {
                var leftId = _availableMods[i].Id;
                var rightId = incomingList[i].Id;
                if (!string.Equals(leftId, rightId, StringComparison.OrdinalIgnoreCase))
                {
                    sameOrder = false;
                    break;
                }
            }

            if (sameOrder)
            {
                return;
            }
        }

        _availableMods.Clear();
        foreach (var mod in incomingList)
        {
            _availableMods.Add(mod);
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