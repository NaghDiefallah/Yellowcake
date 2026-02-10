using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using Yellowcake.Services;

namespace Yellowcake.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private async Task TestConnectivity()
    {
        NotificationService.Instance.Info("Testing connection...");
        
        var isAvailable = await NetworkMonitor.Instance.TestManifestConnectivity(_manifestService.TargetUrl);
        
        if (isAvailable)
        {
            IsOnline = true;
            NotificationService.Instance.Success("Connection restored!");
            await RefreshModsAsync();
        }
        else
        {
            NotificationService.Instance.Warning("Still offline. Check your internet connection.");
        }
    }
}