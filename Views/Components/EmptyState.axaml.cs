using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Windows.Input;

namespace Yellowcake.Views.Components;

public partial class EmptyState : UserControl
{
    public static readonly StyledProperty<StreamGeometry?> IconDataProperty =
        AvaloniaProperty.Register<EmptyState, StreamGeometry?>(nameof(IconData));

    public static readonly StyledProperty<string> TitleTextProperty =
        AvaloniaProperty.Register<EmptyState, string>(nameof(TitleText), "No items");

    public static readonly StyledProperty<string> DescriptionTextProperty =
        AvaloniaProperty.Register<EmptyState, string>(nameof(DescriptionText), string.Empty);

    public static readonly StyledProperty<string> ActionTextProperty =
        AvaloniaProperty.Register<EmptyState, string>(nameof(ActionText), string.Empty);

    public static readonly StyledProperty<ICommand?> ActionCommandProperty =
        AvaloniaProperty.Register<EmptyState, ICommand?>(nameof(ActionCommand));

    public StreamGeometry? IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public string TitleText
    {
        get => GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public string DescriptionText
    {
        get => GetValue(DescriptionTextProperty);
        set => SetValue(DescriptionTextProperty, value);
    }

    public string ActionText
    {
        get => GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    public EmptyState()
    {
        InitializeComponent();
        
        Icon.Bind(PathIcon.DataProperty, this.GetObservable(IconDataProperty));
        Title.Bind(TextBlock.TextProperty, this.GetObservable(TitleTextProperty));
        Description.Bind(TextBlock.TextProperty, this.GetObservable(DescriptionTextProperty));
        ActionButton.Bind(ContentControl.ContentProperty, this.GetObservable(ActionTextProperty));
        ActionButton.Bind(Button.CommandProperty, this.GetObservable(ActionCommandProperty));

        // Toggle visibility based on ActionText without Reactive extensions
        this.GetObservable(ActionTextProperty).Subscribe(new ActionObserver<string>(text =>
        {
            ActionButton.IsVisible = !string.IsNullOrEmpty(text);
        }));
    }

    private sealed class ActionObserver<T> : IObserver<T>
    {
        private readonly Action<T> _onNext;
        public ActionObserver(Action<T> onNext) => _onNext = onNext;
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(T value) => _onNext(value);
    }
}