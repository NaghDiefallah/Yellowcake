using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;
using Yellowcake.Services;

namespace Yellowcake.ViewModels;

public partial class MainViewModel
{
    public void HandleKeyDown(KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        try
        {
            switch (e.Key)
            {
                // F1 or ? - Show shortcuts
                case Key.F1:
                case Key.OemQuestion when !ctrl && !shift:
                    ShowHotkeysGuide();
                    e.Handled = true;
                    break;

                // F5 - Refresh
                case Key.F5:
                    _ = RefreshAllAsync();
                    e.Handled = true;
                    break;

                // Ctrl+F - Focus search
                case Key.F when ctrl:
                    FocusSearch();
                    e.Handled = true;
                    break;

                // Ctrl+, - Settings
                case Key.OemComma when ctrl:
                    IsSettingsOpen = !IsSettingsOpen;
                    e.Handled = true;
                    break;

                // Ctrl+L - Log Viewer
                case Key.L when ctrl:
                    IsLogViewerOpen = !IsLogViewerOpen;
                    e.Handled = true;
                    break;

                // Ctrl+P - Performance Dashboard
                case Key.P when ctrl:
                    IsPerformanceDashboardOpen = !IsPerformanceDashboardOpen;
                    e.Handled = true;
                    break;

                // Ctrl+Shift+D - Diagnostics
                case Key.D when ctrl && shift:
                    ShowDiagnostics();
                    e.Handled = true;
                    break;

                // Ctrl+B - Toggle batch mode
                case Key.B when ctrl:
                    IsBatchMode = !IsBatchMode;
                    e.Handled = true;
                    break;

                // Ctrl+A - Select all (batch mode)
                case Key.A when ctrl && IsBatchMode:
                    SelectAllMods();
                    e.Handled = true;
                    break;

                // Escape - Close overlays
                case Key.Escape:
                    CloseAllOverlays();
                    e.Handled = true;
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error handling keyboard shortcut");
        }
    }

    private void ShowHotkeysGuide()
    {
        var window = new Views.HotkeysWindow();
        window.Show();
    }

    private void ShowDiagnostics()
    {
        var window = new Views.DiagnosticsWindow
        {
            DataContext = new DiagnosticsViewModel(_db)
        };
        window.Show();
    }

    private void CloseAllOverlays()
    {
        IsSettingsOpen = false;
        IsPerformanceDashboardOpen = false;
        IsLogViewerOpen = false;
    }

    [RelayCommand]
    private void SelectAllMods()
    {
        if (!IsBatchMode) IsBatchMode = true;

        _selectedMods.Clear();
        foreach (var mod in _availableMods)
        {
            _selectedMods.Add(mod);
            mod.IsSelectedInBatch = true;
        }
    }

    private void FocusSearch()
    {
        SearchFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        try
        {
            IsBusy = true;
            BusyMessage = "Refreshing...";
            GameStatus = "Refreshing...";

            // Load available mods and sync installed states
            await LoadAvailableModsAsync();
            await SyncInstalledStates();
            
            // Check for updates on installed mods
            var installedWithUpdates = 0;
            foreach (var mod in InstalledMods)
            {
                var remoteMod = _allRemoteMods.FirstOrDefault(r => r.Id == mod.Id);
                if (remoteMod != null && remoteMod.Version != mod.InstalledVersionString)
                {
                    mod.HasUpdate = true;
                    installedWithUpdates++;
                }
            }

            await RefreshUI();

            var message = installedWithUpdates > 0 
                ? $"Refresh complete. {installedWithUpdates} update(s) available."
                : "Refresh complete. All mods are up to date.";
            
            NotificationService.Instance.Success(message);
            Log.Information("Manual refresh completed");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to refresh");
            NotificationService.Instance.Error("Failed to refresh: " + ex.Message);
            GameStatus = "Error";
        }
        finally
        {
            IsBusy = false;
            if (GameStatus == "Refreshing...")
                GameStatus = "Ready";
        }
    }

    // Event for requesting search focus from the view
    public event EventHandler? SearchFocusRequested;
}