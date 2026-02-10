using Avalonia.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Yellowcake.Models;

namespace Yellowcake.ViewModels;

public partial class MainViewModel
{
    public string GamePath
    {
        get => _gamePath;
        set
        {
            if (SetProperty(ref _gamePath, value))
            {
                //InstallSelectedVersionCommand?.NotifyCanExecuteChanged();
            }
        }
    }

    public string GameStatus
    {
        get => _gameStatus;
        set => SetProperty(ref _gameStatus, value);
    }

    public bool IsGameDetected
    {
        get => _isGameDetected;
        set => SetProperty(ref _isGameDetected, value);
    }

    public bool IsBepInstalled
    {
        get => _isBepInstalled;
        set => SetProperty(ref _isBepInstalled, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetProperty(ref _selectedTheme, value) && !string.IsNullOrEmpty(value))
            {
                _db.SaveSetting(ThemeConfigKey, value);
                _themeService.ApplyTheme(value);
            }
        }
    }

    public ObservableCollection<string> AvailableThemes
    {
        get => _availableThemes;
        set => SetProperty(ref _availableThemes, value);
    }

    public ObservableCollection<Mod> InstalledMods
    {
        get => _installedMods;
        set => SetProperty(ref _installedMods, value);
    }

    public ObservableCollection<Mod> AvailableMods
    {
        get => _availableMods;
        set => SetProperty(ref _availableMods, value);
    }

    public bool IsOnline
    {
        get => _isOnline;
        set => SetProperty(ref _isOnline, value);
    }

    public Mod? SelectedMod
    {
        get => _selectedMod;
        set => SetProperty(ref _selectedMod, value);
    }

    public bool IsSearchFocused
    {
        get => _isSearchFocused;
        set => SetProperty(ref _isSearchFocused, value);
    }

    public bool IsBatchMode
    {
        get => _isBatchMode;
        set
        {
            if (SetProperty(ref _isBatchMode, value))
            {
                if (!value)
                {
                    SelectedMods.Clear();
                }
            }
        }
    }

    public AvaloniaList<Mod> SelectedMods
    {
        get => _selectedMods;
        set => SetProperty(ref _selectedMods, value);
    }

    public bool IsDetailsOpen
    {
        get => _isDetailsOpen;
        set => SetProperty(ref _isDetailsOpen, value);
    }

    public Mod? DetailsMod
    {
        get => _detailsMod;
        set => SetProperty(ref _detailsMod, value);
    }

    public string ModChangelog
    {
        get => _modChangelog;
        set => SetProperty(ref _modChangelog, value);
    }

    public List<Mod> ModDependencies
    {
        get => _modDependencies;
        set => SetProperty(ref _modDependencies, value);
    }

    public List<Mod> ModConflicts
    {
        get => _modConflicts;
        set => SetProperty(ref _modConflicts, value);
    }

    public string DependencyGraph
    {
        get => _dependencyGraph;
        set => SetProperty(ref _dependencyGraph, value);
    }

    public ModFilter ActiveFilter
    {
        get => _activeFilter;
        set
        {
            if (SetProperty(ref _activeFilter, value))
            {
                _ = ApplyFilters();
            }
        }
    }

    public string? SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                _searchDebounceTokenSource?.Cancel();
                _searchDebounceTokenSource = new System.Threading.CancellationTokenSource();
                var token = _searchDebounceTokenSource.Token;

                System.Threading.Tasks.Task.Delay(300, token).ContinueWith(t =>
                {
                    if (!t.IsCanceled)
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = ApplyFilters());
                }, System.Threading.Tasks.TaskScheduler.Default);
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool IsFiltering
    {
        get => _isFiltering;
        set => SetProperty(ref _isFiltering, value);
    }

    public string CurrentSort
    {
        get => _currentSort;
        set => SetProperty(ref _currentSort, value);
    }

    public KeyValuePair<string, string> SelectedSource
    {
        get => ManifestSources.FirstOrDefault(s => s.Key == SelectedSourceName);
        set
        {
            if (SelectedSourceName != value.Key)
            {
                SelectedSourceName = value.Key;
                OnPropertyChanged(nameof(SelectedSource));
                OnSelectedSourceChanged(value);
            }
        }
    }

    private string _gamePath = "Not Set";
    private string _gameStatus = "Initializing...";
    private bool _isGameDetected;
    private bool _isBepInstalled;
    private int _selectedTabIndex;
    private string _selectedTheme = "Dark";
    private ObservableCollection<string> _availableThemes = new();
    private ObservableCollection<Mod> _installedMods = new();
    private ObservableCollection<Mod> _availableMods = new();
    private bool _isOnline = true;
    private Mod? _selectedMod;
    private bool _isSearchFocused;
    private bool _isBatchMode;
    private AvaloniaList<Mod> _selectedMods = new();
    private bool _isDetailsOpen;
    private Mod? _detailsMod;
    private string _modChangelog = string.Empty;
    private List<Mod> _modDependencies = new();
    private List<Mod> _modConflicts = new();
    private string _dependencyGraph = string.Empty;
    private ModFilter _activeFilter = ModFilter.All;
    private string? _searchText;
    private bool _isLoading;
    private bool _isFiltering;
    private string _currentSort = "NameAsc";

    partial void OnSelectedSourceChanged(KeyValuePair<string, string> value);
}