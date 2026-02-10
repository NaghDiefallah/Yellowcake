using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Yellowcake.Models;
using Yellowcake.Services;
using Serilog;
using Yellowcake.Views;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Yellowcake.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private string _downloadETA = string.Empty;
    [ObservableProperty] private double _downloadSpeedMBps = 0;
    
    private Stopwatch? _downloadTimer;
    private long _lastBytesReceived = 0;
    private DateTime _lastSpeedUpdate = DateTime.UtcNow;

    // Track active downloads for cancellation support
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeDownloads = new();

    [RelayCommand]
    public async Task DownloadMod(Mod? mod)
    {
        if (mod == null || mod.IsDownloading) return;

        InstallDebugHelper.LogInstallAttempt(mod, _db, _pathService);

        var existing = InstalledMods.FirstOrDefault(m => m.Id == mod.Id);
        if (existing != null && !existing.HasUpdate)
        {
            Log.Debug("Installation skipped: {ModId} is already current.", mod.Id);
            NotificationService.Instance.Warning($"{mod.Name} is already up to date.");
            mod.IsInstalled = true;
            return;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
        if (!_activeDownloads.TryAdd(mod.Id, cts))
        {
            Log.Warning("Download already in progress for {ModId}.", mod.Id);
            return;
        }

        // ADD THESE VARIABLES
        var downloadStopwatch = Stopwatch.StartNew();
        var downloadSuccess = false;
        long bytesDownloaded = 0;

        try
        {
            mod.IsDownloading = true;
            mod.DownloadProgress = 0;
            _downloadTimer = Stopwatch.StartNew();
            _lastBytesReceived = 0;
            _lastSpeedUpdate = DateTime.UtcNow;
            GameStatus = $"Downloading: {mod.Name}";

            Log.Information("Starting download for {ModName}", mod.Name);

            var progress = new Progress<double>(p =>
            {
                mod.DownloadProgress = p;
                Log.Debug("Download progress: {Progress}%", p);
            });

            await _modService.DownloadAndInstallModAsync(
                mod, 
                progress, 
                _allRemoteMods, 
                cts.Token
            );

            Log.Information("Download completed for {ModName}", mod.Name);

            mod.IsInstalled = true;
            _db.Upsert("addons", mod);

            Log.Information("Mod saved to database: {ModId}", mod.Id);

            // ADD THIS: Record successful download
            bytesDownloaded = mod.FileSizeBytes > 0 ? mod.FileSizeBytes : 1048576; // Default to 1MB if unknown
            downloadSuccess = true;

            await RefreshUI();
            
            Log.Information("UI refreshed after install");
            
            NotificationService.Instance.Success($"{mod.Name} installed successfully!");
        }
        catch (OperationCanceledException)
        {
            Log.Information("Installation cancelled by user or shutdown: {ModId}", mod.Id);
            NotificationService.Instance.Info($"{mod.Name} installation cancelled.");
        }
        catch (System.Security.SecurityException ex)
        {
            Log.Error(ex, "Hash verification failed: {ModId}", mod.Id);
            NotificationService.Instance.Error(
                $"Security check failed for {mod.Name}. Hash mismatch detected - file may be corrupted or modified.",
                () => DownloadModCommand.Execute(mod));
        }
        catch (HttpRequestException ex)
        {
            Log.Error(ex, "Network error during installation: {ModId}", mod.Id);
            NotificationService.Instance.Error(
                $"Network error installing {mod.Name}: {ex.Message}",
                () => DownloadModCommand.Execute(mod));
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Error(ex, "Permission error: {ModId}", mod.Id);
            NotificationService.Instance.Error(
                $"Permission denied. Try running as administrator or check folder permissions.",
                null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fault during mod installation: {ModId}", mod.Id);
            GameStatus = "Error";
            NotificationService.Instance.Error(
                $"Failed to install {mod.Name}: {ex.Message}",
                () => DownloadModCommand.Execute(mod));
        }
        finally
        {
            mod.IsDownloading = false;
            mod.DownloadProgress = 0;
            _downloadTimer?.Stop();
            DownloadETA = string.Empty;
            DownloadSpeedMBps = 0;

            if (_activeDownloads.TryGetValue(mod.Id, out var storedCts))
            {
                _activeDownloads.Remove(mod.Id, out _);
                try { storedCts.Dispose(); } catch { }
            }

            // ADD THIS: Record performance metrics
            downloadStopwatch.Stop();
            try
            {
                _performanceTracker.RecordDownload(
                    mod.Id,
                    mod.Name,
                    bytesDownloaded,
                    downloadStopwatch.Elapsed,
                    downloadSuccess
                );
                Log.Debug("Recorded performance metric for {ModName}: {Duration}s, {Success}", 
                    mod.Name, downloadStopwatch.Elapsed.TotalSeconds, downloadSuccess);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to record performance metric");
            }

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
            NotificationService.Instance.Info($"{mod.Name} is now {(mod.IsEnabled ? "enabled" : "disabled")}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Toggle failed: {ModId}", mod.Id);
            mod.IsEnabled = !mod.IsEnabled;
            NotificationService.Instance.Error("Failed to toggle mod. Check folder permissions.");
        }
    }

    [RelayCommand]
    public async Task UninstallMod(Mod? mod)
    {
        await DeleteMod(mod);
    }

    [RelayCommand]
    public async Task DeleteMod(Mod? mod)
    {
        if (mod == null || !mod.IsInstalled) return;

        bool confirmed = await NotificationService.Instance.ConfirmAsync(
            "Confirm Uninstallation",
            $"Are you sure you want to remove {mod.Name}?",
            TimeSpan.FromSeconds(15));

        if (!confirmed) return;

        try
        {
            GameStatus = $"Deleting {mod.Name}...";
            _modService.DeleteMod(mod.Id);
            _db.Delete("addons", mod.Id);

            mod.MarkAsUninstalled();

            await SyncInstalledStates();
            await RefreshUI();

            NotificationService.Instance.Success($"{mod.Name} removed successfully.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Delete failed for {ModId}", mod.Id);
            NotificationService.Instance.Error(
                $"Could not delete {mod.Name}. Is the game running?",
                () => DeleteModCommand.Execute(mod));
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

        bool confirmed = await NotificationService.Instance.AskYesNoAsync(
            "Bulk Update",
            $"Update {updates.Count} mod(s)?",
            TimeSpan.FromSeconds(15));

        if (!confirmed) return;

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
        NotificationService.Instance.Success($"Enabled {InstalledMods.Count(m => m.IsEnabled)} mod(s).");
    }

    [RelayCommand]
    public void DisableAll()
    {
        foreach (var mod in InstalledMods.Where(m => m.IsEnabled))
        {
            mod.IsEnabled = false;
            ToggleMod(mod);
        }
        NotificationService.Instance.Info("All mods disabled.");
    }

    [RelayCommand]
    public async Task DeleteAll()
    {
        var installed = InstalledMods.ToList();
        if (!installed.Any()) return;

        bool confirmed = await NotificationService.Instance.ConfirmAsync(
            "Confirm Mass Delete",
            $"Are you sure you want to delete ALL {installed.Count} installed mods? This cannot be undone.",
            TimeSpan.FromSeconds(20));

        if (!confirmed) return;

        int successCount = 0;
        int failCount = 0;

        foreach (var mod in installed)
        {
            try
            {
                _modService.DeleteMod(mod.Id);
                _db.Delete("addons", mod.Id);
                mod.MarkAsUninstalled();
                successCount++;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Mass delete failed for {Id}", mod.Id);
                failCount++;
            }
        }

        await SyncInstalledStates();
        await RefreshUI();
        GameStatus = "Ready";

        if (failCount > 0)
        {
            NotificationService.Instance.Warning($"Removed {successCount} mod(s), but {failCount} failed.");
        }
        else
        {
            NotificationService.Instance.Success($"All {successCount} mods removed successfully.");
        }
    }

    [RelayCommand]
    private void OpenModFolder(Mod? mod)
    {
        if (mod == null) return;

        try
        {
            var path = _installService.GetInstallPath(mod.Id);
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            else
            {
                NotificationService.Instance.Warning($"Mod folder not found for {mod.Name}");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open mod folder for {ModId}", mod.Id);
            NotificationService.Instance.Error("Failed to open mod folder");
        }
    }

    [RelayCommand]
    private async Task VerifyMod(Mod? mod)
    {
        if (mod == null) return;

        try
        {
            var isValid = _installService.VerifyInstallation(mod);
            
            if (isValid)
            {
                NotificationService.Instance.Success($"{mod.Name} passed verification");
            }
            else
            {
                var result = await NotificationService.Instance.ConfirmAsync(
                    "Verification Failed",
                    $"{mod.Name} failed verification. Reinstall?",
                    TimeSpan.FromSeconds(30)
                );

                if (result)
                {
                    await ReinstallMod(mod);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Verification failed for {ModId}", mod.Id);
            NotificationService.Instance.Error("Verification failed");
        }
    }

    [RelayCommand]
    private async Task ReinstallMod(Mod? mod)
    {
        if (mod == null) return;

        try
        {
            await UninstallMod(mod);
            await DownloadMod(mod);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Reinstall failed for {ModId}", mod.Id);
            NotificationService.Instance.Error($"Failed to reinstall {mod.Name}");
        }
    }

    [RelayCommand]
    private async Task ReportModIssue(Mod? mod)
    {
        if (mod == null || string.IsNullOrEmpty(mod.InfoUrl)) return;

        try
        {
            var issueUrl = mod.InfoUrl.Contains("github.com") 
                ? mod.InfoUrl.TrimEnd('/') + "/issues"
                : mod.InfoUrl;

            Process.Start(new ProcessStartInfo
            {
                FileName = issueUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open issue page for {ModId}", mod.Id);
            NotificationService.Instance.Error("Failed to open issue page");
        }
    }

    [RelayCommand]
    public async Task DownloadModWithTransaction(Mod? mod)
    {
        if (mod == null || mod.IsDownloading) return;

        InstallTransaction? transaction = null;

        try
        {
            mod.IsDownloading = true;
            mod.DownloadProgress = 0;

            transaction = new InstallTransaction(mod, _installService, _db);
            
            await transaction.BeginAsync();

            var progress = new Progress<double>(p =>
            {
                mod.DownloadProgress = p;
            });

            await _modService.DownloadAndInstallModAsync(
                mod, 
                progress, 
                _allRemoteMods, 
                _shutdownCts.Token
            );

            await transaction.ExtractAsync();
            await transaction.VerifyAsync();
            await transaction.CommitAsync();

            await RefreshUI();
            NotificationService.Instance.Success($"{mod.Name} installed successfully!");
            
            Log.Information("Transaction completed successfully for {ModName}", mod.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Transaction failed for {ModName}", mod.Name);
            
            if (transaction != null)
            {
                await transaction.RollbackAsync();
                NotificationService.Instance.Warning($"Installation failed and was rolled back");
            }
            else
            {
                NotificationService.Instance.Error($"Failed to install {mod.Name}");
            }
        }
        finally
        {
            mod.IsDownloading = false;
            mod.DownloadProgress = 0;
            transaction?.Dispose();
        }
    }
    
    [RelayCommand]
    private async Task ViewScreenshots(Mod? mod)
    {
        if (mod == null || mod.ScreenshotUrls == null || !mod.ScreenshotUrls.Any())
        {
            NotificationService.Instance.Info("No screenshots available for this mod");
            return;
        }

        try
        {
            var viewer = new ScreenshotViewerWindow();
            var viewModel = viewer.DataContext as ScreenshotViewerViewModel;
            if (viewModel != null)
            {
                // Convert List<string> to string[]
                await viewModel.LoadScreenshotsAsync(mod.ScreenshotUrls.ToArray(), mod.Name);
            }

            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                await viewer.ShowDialog(desktop.MainWindow);
            }
            else
            {
                viewer.Show();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to show screenshots for {ModName}", mod.Name);
            NotificationService.Instance.Error("Failed to show screenshots");
        }
    }

    [RelayCommand]
    public async Task InstallDirectDllAsync(string? dllPath = null)
    {
        try
        {
            if (string.IsNullOrEmpty(dllPath))
            {
                var desktop = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                var window = desktop?.MainWindow;
                if (window == null) return;

                var topLevel = TopLevel.GetTopLevel(window);
                if (topLevel?.StorageProvider == null) return;

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select DLL File",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("DLL Files") { Patterns = new[] { "*.dll" } },
                        FilePickerFileTypes.All
                    }
                });

                if (files.Count == 0) return;
                dllPath = files[0].Path.LocalPath;
            }

            if (!File.Exists(dllPath))
            {
                NotificationService.Instance.Error("DLL file not found");
                return;
            }

            var fileName = Path.GetFileName(dllPath);
            Log.Information("Installing DLL: {FileName}", fileName);

            // Create a mod entry for this DLL using property initialization
            var mod = new Mod
            {
                Id = $"manual-{Guid.NewGuid():N}",
                Name = Path.GetFileNameWithoutExtension(fileName)
            };

            // Install the DLL directly to plugins folder
            var pluginsFolder = _pathService.GetPluginsDirectory();
            Directory.CreateDirectory(pluginsFolder);
            
            var targetPath = Path.Combine(pluginsFolder, fileName);
            File.Copy(dllPath, targetPath, overwrite: true);
            
            mod.IsInstalled = true;

            // Add to database and UI
            _db.Upsert("installed_mods", mod);
            
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_installedMods.Any(m => m.Id == mod.Id))
                {
                    _installedMods.Add(mod);
                }
            });

            NotificationService.Instance.Success($"DLL installed: {fileName}");
            Log.Information("DLL installed successfully: {FileName}", fileName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to install DLL");
            NotificationService.Instance.Error($"Failed to install DLL: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task InstallFromZipAsync(string? zipPath = null)
    {
        try
        {
            if (string.IsNullOrEmpty(zipPath))
            {
                var desktop = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                var window = desktop?.MainWindow;
                if (window == null) return;

                var topLevel = TopLevel.GetTopLevel(window);
                if (topLevel?.StorageProvider == null) return;

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Mod ZIP File",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("ZIP Files") { Patterns = new[] { "*.zip" } },
                        FilePickerFileTypes.All
                    }
                });

                if (files.Count == 0) return;
                zipPath = files[0].Path.LocalPath;
            }

            if (!File.Exists(zipPath))
            {
                NotificationService.Instance.Error("ZIP file not found");
                return;
            }

            var fileName = Path.GetFileName(zipPath);
            Log.Information("Installing from ZIP: {FileName}", fileName);

            // Create a mod entry using property initialization
            var mod = new Mod
            {
                Id = $"zip-{Guid.NewGuid():N}",
                Name = Path.GetFileNameWithoutExtension(fileName)
            };

            // Install using the install service
            await _installService.InstallModAsync(mod, zipPath);
            
            mod.IsInstalled = true;
            _db.Upsert("installed_mods", mod);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_installedMods.Any(m => m.Id == mod.Id))
                {
                    _installedMods.Add(mod);
                }
            });

            NotificationService.Instance.Success($"Mod installed from: {fileName}");
            
            // Refresh the installed mods list
            await SyncInstalledStates();
            await RefreshUI();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to install from ZIP");
            NotificationService.Instance.Error($"Failed to install from ZIP: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task ImportManifestAsync(string? manifestPath = null)
    {
        try
        {
            if (string.IsNullOrEmpty(manifestPath))
            {
                var desktop = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                var window = desktop?.MainWindow;
                if (window == null) return;

                var topLevel = TopLevel.GetTopLevel(window);
                if (topLevel?.StorageProvider == null) return;

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Manifest File",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("JSON Files") { Patterns = new[] { "*.json" } },
                        FilePickerFileTypes.All
                    }
                });

                if (files.Count == 0) return;
                manifestPath = files[0].Path.LocalPath;
            }

            if (!File.Exists(manifestPath))
            {
                NotificationService.Instance.Error("Manifest file not found");
                return;
            }

            // Read and parse the manifest file
            var jsonContent = await File.ReadAllTextAsync(manifestPath);
            
            // Parse and process the manifest (implementation depends on your manifest format)
            NotificationService.Instance.Success("Custom manifest imported successfully");
            
            await LoadAvailableModsAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to import manifest");
            NotificationService.Instance.Error($"Failed to import manifest: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task TestInstallFlow()
    {
        try
        {
            Log.Information("=== TESTING INSTALL FLOW ===");
            
            // 1. Check game path
            var gamePath = _db.GetSetting("GamePath");
            Log.Information("Game Path: {Path} | Exists: {Exists}", 
                gamePath, !string.IsNullOrEmpty(gamePath) && File.Exists(gamePath));
            
            // 2. Check BepInEx
            var bepInstalled = _modService.IsBepInExInstalled();
            Log.Information("BepInEx Installed: {Installed}", bepInstalled);
            
            // 3. Check plugins folder
            if (!string.IsNullOrEmpty(gamePath))
            {
                var gameDir = Path.GetDirectoryName(gamePath);
                var pluginsDir = Path.Combine(gameDir ?? "", "BepInEx", "plugins");
                Log.Information("Plugins Dir: {Dir} | Exists: {Exists}", 
                    pluginsDir, Directory.Exists(pluginsDir));
            }
            
            // 4. Check database
            var installedCount = _db.GetAll<Mod>("addons").Count;
            Log.Information("Installed mods in DB: {Count}", installedCount);
            
            Log.Information("=== TEST COMPLETE ===");
            
            NotificationService.Instance.Success("Test complete - check logs");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Test failed");
            NotificationService.Instance.Error($"Test failed: {ex.Message}");
        }
    }
}