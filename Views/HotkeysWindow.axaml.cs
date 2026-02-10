using Avalonia.Controls;
using Yellowcake.ViewModels;

namespace Yellowcake.Views;

public partial class HotkeysWindow : Window
{
    public HotkeysWindow()
    {
        InitializeComponent();
        DataContext = new HotkeysViewModel();
    }
}