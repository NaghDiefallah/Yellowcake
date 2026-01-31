using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Yellowcake.Services;
using Yellowcake.ViewModels;

namespace Yellowcake;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        NotificationService.Instance.Initialize(this);

        // HeaderDragArea.PointerPressed += OnHeaderPointerPressed;
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var source = e.Source as Control;
            if (source is not Button && source is not ComboBox && source is not TextBox)
            {
                BeginMoveDrag(e);
            }
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainViewModel { MinimizeToTray: true })
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }
}