using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace Yellowcake.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallSelectedVersionCommand))]
    private string? _selectedBepInExVersion;

    [ObservableProperty] 
    private ObservableCollection<string> _bepInExVersions = new();

    [ObservableProperty] 
    private double _bepInExDownloadProgress;
}