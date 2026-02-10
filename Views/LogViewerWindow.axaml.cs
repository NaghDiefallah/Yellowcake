using Avalonia.Controls;
using Yellowcake.ViewModels;

namespace Yellowcake.Views;

public partial class LogViewerWindow : UserControl
{
    public LogViewerWindow()
    {
        InitializeComponent();
        DataContext = new LogViewerViewModel();
    }
}