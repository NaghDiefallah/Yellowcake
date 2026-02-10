using Avalonia.Controls;
using Yellowcake.ViewModels;

namespace Yellowcake.Views;

public partial class PerformanceDashboard : UserControl
{
    public PerformanceDashboard()
    {
        InitializeComponent();
        
        // Initialize ViewModel
        DataContext = new PerformanceDashboardViewModel();
        
        // Refresh stats when the control is loaded
        Loaded += (s, e) =>
        {
            if (DataContext is PerformanceDashboardViewModel vm)
            {
                vm.RefreshCommand.Execute(null);
            }
        };
    }
}