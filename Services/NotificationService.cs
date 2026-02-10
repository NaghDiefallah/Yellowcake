using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Yellowcake.Services;

public interface INotificationService
{
    void Initialize(Visual visual);
    void Success(string message, TimeSpan? expiration = null);
    void Error(string message, Action? retryAction = null, TimeSpan? expiration = null);
    void Info(string message, TimeSpan? expiration = null);
    void Warning(string message, TimeSpan? expiration = null);
    void Show(string title, string message, NotificationType type = NotificationType.Information, TimeSpan? expiration = null, Action? onClick = null);
    Task<bool> AskYesNoAsync(string title, string message, TimeSpan? timeout = null);
    Task<bool> ConfirmAsync(string title, string message, TimeSpan? timeout = null);
    void Clear();
}

public class NotificationService : INotificationService
{
    private static readonly Lazy<NotificationService> _lazy = new(() => new NotificationService());
    public static NotificationService Instance => _lazy.Value;

    private WindowNotificationManager? _manager;
    private readonly ConcurrentDictionary<string, ConfirmationContext> _pending = new();
    private int _count;

    private const int MaxNotifications = 5;

    private sealed class ConfirmationContext
    {
        public TaskCompletionSource<bool> Tcs { get; } = new();
        public CancellationTokenSource Cts { get; } = new();
        public string Id { get; init; } = Guid.NewGuid().ToString();
    }

    public void Initialize(Visual visual)
    {
        var topLevel = TopLevel.GetTopLevel(visual);
        if (topLevel == null)
        {
            Log.Warning("[NotificationService] TopLevel is null");
            return;
        }

        _manager = new WindowNotificationManager(topLevel)
        {
            Position = NotificationPosition.BottomRight,
            MaxItems = MaxNotifications,
            Margin = new Thickness(0, 0, 15, 15)
        };

        Log.Information("[NotificationService] Initialized");
    }

    public void Show(string title, string message, NotificationType type = NotificationType.Information, TimeSpan? expiration = null, Action? onClick = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Show(title, message, type, expiration, onClick));
            return;
        }

        if (_manager == null)
        {
            Log.Warning("[NotificationService] Not initialized");
            return;
        }

        if (Interlocked.Increment(ref _count) > MaxNotifications)
        {
            Interlocked.Decrement(ref _count);
            return;
        }

        var notification = new Notification(
            title,
            message,
            type,
            expiration ?? GetExpiration(type),
            onClick,
            () => Interlocked.Decrement(ref _count));

        _manager.Show(notification);
        Log.Debug("[NotificationService] {Title}", title);
    }

    public void Success(string message, TimeSpan? expiration = null)
    {
        Log.Information("[Notification] Success: {Message}", message);
        Show("✓ Success", message, NotificationType.Success, expiration);
    }

    public void Error(string message, Action? retryAction = null, TimeSpan? expiration = null)
    {
        Log.Error("[Notification] Error: {Message}", message);
        
        if (retryAction != null)
        {
            Show("✗ Error", $"{message}\n\n👆 Click to retry", NotificationType.Error, expiration ?? TimeSpan.FromSeconds(10), retryAction);
        }
        else
        {
            Show("✗ Error", message, NotificationType.Error, expiration ?? TimeSpan.FromSeconds(10));
        }
    }

    public void Info(string message, TimeSpan? expiration = null)
    {
        Log.Information("[Notification] Info: {Message}", message);
        Show("ⓘ Information", message, NotificationType.Information, expiration);
    }

    public void Warning(string message, TimeSpan? expiration = null)
    {
        Log.Warning("[Notification] Warning: {Message}", message);
        Show("⚠ Warning", message, NotificationType.Warning, expiration);
    }

    public async Task<bool> AskYesNoAsync(string title, string message, TimeSpan? timeout = null)
    {
        var context = new ConfirmationContext();
        _pending.TryAdd(context.Id, context);

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(15);

        Dispatcher.UIThread.Post(() =>
        {
            void HandleYes()
            {
                if (_pending.TryRemove(context.Id, out var ctx))
                {
                    ctx.Tcs.TrySetResult(true);
                    ctx.Cts.Cancel();
                    Log.Debug("[NotificationService] YES: {Title}", title);
                }
            }

            Show(
                $"❓ {title}",
                $"{message}\n\n👆 Click = YES | Auto-dismiss = NO ({effectiveTimeout.TotalSeconds:F0}s)",
                NotificationType.Information,
                effectiveTimeout,
                HandleYes);
        });

        _ = Task.Delay(effectiveTimeout, context.Cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled && _pending.TryRemove(context.Id, out var ctx))
            {
                ctx.Tcs.TrySetResult(false);
                Log.Debug("[NotificationService] Timeout NO: {Title}", title);
            }
        }, TaskScheduler.Default);

        return await context.Tcs.Task;
    }

    public async Task<bool> ConfirmAsync(string title, string message, TimeSpan? timeout = null)
    {
        var context = new ConfirmationContext();
        _pending.TryAdd(context.Id, context);

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(20);

        Dispatcher.UIThread.Post(() =>
        {
            void HandleConfirm()
            {
                if (_pending.TryRemove(context.Id, out var ctx))
                {
                    ctx.Tcs.TrySetResult(true);
                    ctx.Cts.Cancel();
                    Log.Debug("[NotificationService] CONFIRMED: {Title}", title);
                }
            }

            Show(
                $"⚠ {title}",
                $"{message}\n\n👆 Click = CONFIRM | Auto-dismiss = CANCEL ({effectiveTimeout.TotalSeconds:F0}s)",
                NotificationType.Warning,
                effectiveTimeout,
                HandleConfirm);
        });

        _ = Task.Delay(effectiveTimeout, context.Cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled && _pending.TryRemove(context.Id, out var ctx))
            {
                ctx.Tcs.TrySetResult(false);
                Log.Debug("[NotificationService] Timeout CANCEL: {Title}", title);
            }
        }, TaskScheduler.Default);

        return await context.Tcs.Task;
    }

    public void Clear()
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var ctx in _pending.Values)
            {
                ctx.Tcs.TrySetResult(false);
                ctx.Cts.Cancel();
            }
            _pending.Clear();
            _count = 0;
            Log.Debug("[NotificationService] Cleared");
        });
    }

    private static TimeSpan GetExpiration(NotificationType type)
    {
        return type switch
        {
            NotificationType.Success => TimeSpan.FromSeconds(4),
            NotificationType.Information => TimeSpan.FromSeconds(5),
            NotificationType.Warning => TimeSpan.FromSeconds(7),
            NotificationType.Error => TimeSpan.FromSeconds(10),
            _ => TimeSpan.FromSeconds(5)
        };
    }
}