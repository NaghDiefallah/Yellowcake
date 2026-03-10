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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Yellowcake.Models;
using Yellowcake.Services;

namespace Yellowcake.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private bool _isDownloading;
    private volatile bool _isCheckingUpdates;

    [RelayCommand]
    private void CancelBusy()
    {
        foreach (var cts in _activeDownloads.Values)
        {
            try { cts.Cancel(); } catch { }
        }
        IsBusy = false;
        GameStatus = "Cancelled";
        Log.Information("User cancelled all active downloads ({Count} task(s))", _activeDownloads.Count);
    }

    [RelayCommand]
    public async Task UpdateMod(Mod? mod)
    {
        if (mod == null || mod.IsDownloading)
        {
            return;
        }

        if (!_remoteModIndex.TryGetValue(mod.Id, out var remoteMod))
        {
            Log.Warning("Update target {ModId} missing from manifest", mod.Id);
            GameStatus = "Mod not in manifest";
            return;
        }

        GameStatus = $"Updating: {mod.Name}";
        await DownloadMod(remoteMod);
        await SyncInstalledStates();
        await RefreshUI();
        GameStatus = "Ready";
    }

    [RelayCommand]
    public async Task UpdateAllMods()
    {
        var updates = InstalledMods.Where(m => m.HasUpdate).ToList();
        if (updates.Count == 0)
        {
            return;
        }

        var remoteTargets = updates
            .Where(m => !string.IsNullOrWhiteSpace(m.Id) && _remoteModIndex.ContainsKey(m.Id))
            .Select(m => _remoteModIndex[m.Id])
            .ToList();

        if (remoteTargets.Count == 0)
        {
            NotificationService.Instance.Warning("No update targets were found in the current manifest.");
            return;
        }

        var total = remoteTargets.Count;
        Interlocked.Add(ref _activeBulkDownloads, total);
        GameStatus = $"Updating {total} mod(s)...";

        var tasks = remoteTargets.Select(async m =>
        {
            try { await DownloadMod(m); }
            finally
            {
                var remaining = Interlocked.Decrement(ref _activeBulkDownloads);
                if (remaining <= 0)
                    GameStatus = "Ready";
            }
        }).ToList();

        await Task.WhenAll(tasks);

        await SyncInstalledStates();
        await RefreshUI();

        var remainingUpdates = InstalledMods.Count(m => m.HasUpdate);
        var updatedCount = total - remainingUpdates;
        NotificationService.Instance.Success($"Update pass complete. Updated {Math.Max(updatedCount, 0)} mod(s).");
        GameStatus = "Ready";
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

            using (var response = await _http.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                await using (var fileStream = new FileStream(downloadPath, System.IO.FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
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
            var latest = await GetRemoteVersionAsync();
            if (latest == null || !Version.TryParse(CurrentVersion, out var current))
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
                        $"A new version (v{latest}) is available. Open the version endpoint now?",
                        ButtonEnum.YesNo,
                        Icon.Info);

                    return await box.ShowAsync();
                });

                if (result == ButtonResult.Yes)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = OfficialVersionUrl,
                        UseShellExecute = true
                    });
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

    private async Task<Version?> GetRemoteVersionAsync()
    {
        try
        {
            var payload = await _http.GetStringAsync(OfficialVersionUrl);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            var raw = payload.Trim();

            if (raw.StartsWith("\"", StringComparison.Ordinal) && raw.EndsWith("\"", StringComparison.Ordinal))
            {
                var value = JsonSerializer.Deserialize<string>(raw);
                return ParseVersion(value);
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.String)
            {
                return ParseVersion(root.GetString());
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("version", out var versionProp))
                {
                    return ParseVersion(versionProp.GetString());
                }

                if (root.TryGetProperty("latestVersion", out var latestVersionProp))
                {
                    return ParseVersion(latestVersionProp.GetString());
                }

                if (root.TryGetProperty("tag", out var tagProp))
                {
                    return ParseVersion(tagProp.GetString());
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Version endpoint check failed");
            return null;
        }
    }

    private static Version? ParseVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var normalized = raw.Trim().TrimStart('v', 'V');
        return Version.TryParse(normalized, out var version) ? version : null;
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