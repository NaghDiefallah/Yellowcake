using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using Yellowcake.ViewModels;

namespace Yellowcake.Views;

/// <summary>
/// Side drawer displaying detailed information about a selected mod.
/// Features smooth animations, keyboard navigation, and auto-scroll.
/// </summary>
public partial class ModDetailsDrawer : UserControl
{
    private ScrollViewer? _scrollViewer;
    private bool _isInitialized;

    public ModDetailsDrawer()
    {
        InitializeComponent();
        
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;

        _scrollViewer = this.FindDescendantOfType<ScrollViewer>();
        
        if (_scrollViewer != null)
        {
            // Smooth scrolling configuration
            _scrollViewer.PointerWheelChanged += OnMouseWheelScroll;
        }

        // Add keyboard support
        KeyDown += OnKeyDown;
        _isInitialized = true;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_scrollViewer != null)
        {
            _scrollViewer.PointerWheelChanged -= OnMouseWheelScroll;
        }
        KeyDown -= OnKeyDown;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    { if (_isInitialized) return;
        if (_scrollViewer != null && DataContext is MainViewModel vm && vm.IsDetailsOpen)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _scrollViewer.Offset = new Avalonia.Vector(0, 0);
            }, DispatcherPriority.Render);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        switch (e.Key)
        {
            case Key.Escape:
                vm.CloseModDetailsCommand?.Execute(null);
                e.Handled = true;
                break;

            case Key.PageDown:
                _scrollViewer?.PageDown();
                e.Handled = true;
                break;

            case Key.PageUp:
                _scrollViewer?.PageUp();
                e.Handled = true;
                break;
        }
    }

    private void OnMouseWheelScroll(object? sender, PointerWheelEventArgs e)
    {
        if (_scrollViewer == null) return;

        var delta = e.Delta.Y * 50;
        var newOffset = _scrollViewer.Offset.Y - delta;
        
        newOffset = Math.Max(0, Math.Min(newOffset, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height));
        _scrollViewer.Offset = new Avalonia.Vector(0, newOffset);
        
        e.Handled = true;
    }

    private void AnimateEntrance()
    {
        var transform = new TranslateTransform { X = 50 };
        RenderTransform = transform;
        Opacity = 0;

        var slideAnimation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(300),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(TranslateTransform.XProperty, 50.0),
                        new Setter(OpacityProperty, 0.0)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(TranslateTransform.XProperty, 0.0),
                        new Setter(OpacityProperty, 1.0)
                    }
                }
            }
        };

        slideAnimation.RunAsync(this);
    }

    public void ScrollToSection(string sectionName)
    {
        if (_scrollViewer == null) return;

        var textBlocks = this.FindDescendantsOfType<TextBlock>()
            .Where(tb => tb.Text?.Equals(sectionName, StringComparison.OrdinalIgnoreCase) == true);

        var target = textBlocks.FirstOrDefault();
        if (target != null)
        {
            target.BringIntoView();
        }
    }

    public async void AnimateExit(Action onComplete)
    {
        var transform = RenderTransform as TranslateTransform ?? new TranslateTransform();
        RenderTransform = transform;

        var slideOut = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(200),
            Easing = new CubicEaseIn(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(TranslateTransform.XProperty, 0.0),
                        new Setter(OpacityProperty, 1.0)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(TranslateTransform.XProperty, 50.0),
                        new Setter(OpacityProperty, 0.0)
                    }
                }
            }
        };

        await slideOut.RunAsync(this);
        onComplete?.Invoke();
    }
}

internal static class VisualTreeExtensions
{
    public static T? FindDescendantOfType<T>(this Control control) where T : Control
    {
        if (control is T result)
            return result;

        if (control is Panel panel)
        {
            foreach (var child in panel.Children.OfType<Control>())
            {
                var found = FindDescendantOfType<T>(child);
                if (found != null) return found;
            }
        }
        else if (control is ContentControl contentControl && contentControl.Content is Control childControl)
        {
            return FindDescendantOfType<T>(childControl);
        }
        else if (control is Decorator decorator && decorator.Child is Control decoratorChild)
        {
            return FindDescendantOfType<T>(decoratorChild);
        }

        return null;
    }

    public static IEnumerable<T> FindDescendantsOfType<T>(this Control control) where T : Control
    {
        if (control is T result)
            yield return result;

        if (control is Panel panel)
        {
            foreach (var child in panel.Children.OfType<Control>())
            {
                foreach (var descendant in FindDescendantsOfType<T>(child))
                    yield return descendant;
            }
        }
        else if (control is ContentControl contentControl && contentControl.Content is Control childControl)
        {
            foreach (var descendant in FindDescendantsOfType<T>(childControl))
                yield return descendant;
        }
        else if (control is Decorator decorator && decorator.Child is Control decoratorChild)
        {
            foreach (var descendant in FindDescendantsOfType<T>(decoratorChild))
                yield return descendant;
        }
    }
}