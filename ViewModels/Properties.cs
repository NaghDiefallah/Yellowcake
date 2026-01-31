using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using Yellowcake.Models;

namespace Yellowcake.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private string _gameStatus = "Status: Offline";
    [ObservableProperty] private double _bepInExDownloadProgress;

    [ObservableProperty] private ObservableCollection<Mod> _installedMods = [];
    [ObservableProperty] private ObservableCollection<Mod> _availableMods = [];

    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(InstallSelectedVersionCommand))] private string? _gamePath;
    [ObservableProperty] private bool _isGameDetected;
    [ObservableProperty] private bool _isBepInstalled;

    [ObservableProperty] private ObservableCollection<string> _availableThemes = [];
    [ObservableProperty] private string _selectedTheme = string.Empty;
}