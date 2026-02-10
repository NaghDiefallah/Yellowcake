using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Yellowcake.ViewModels;

namespace Yellowcake.Views;

public partial class ScreenshotViewerWindow : Window
{
    public ScreenshotViewerWindow()
    {
        InitializeComponent();
        DataContext = new ScreenshotViewerViewModel();
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Thumbnail_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext != null && DataContext is ScreenshotViewerViewModel vm)
        {
            var index = vm.Screenshots.IndexOf((Avalonia.Media.Imaging.Bitmap)border.DataContext);
            if (index >= 0)
            {
                vm.CurrentIndex = index;
                vm.CurrentScreenshot = vm.Screenshots[index];
            }
        }
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ScreenshotViewerViewModel vm) return;

        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Left:
                if (vm.CanGoPrevious) vm.PreviousCommand.Execute(null);
                break;
            case Key.Right:
                if (vm.CanGoNext) vm.NextCommand.Execute(null);
                break;
        }
    }
}