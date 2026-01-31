using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using System;

namespace Yellowcake.Services;

public class NotificationService
{
    private static NotificationService? _instance;
    public static NotificationService Instance => _instance ??= new NotificationService();

    private WindowNotificationManager? _notificationManager;

    public void Initialize(Visual visual)
    {
        var topLevel = TopLevel.GetTopLevel(visual);
        if (topLevel == null) return;

        _notificationManager = new WindowNotificationManager(topLevel)
        {
            Position = NotificationPosition.BottomCenter,
            MaxItems = 3,
            Margin = new Thickness(0, 0, 0, 20)
        };
    }

    public void Show(string title, string message, NotificationType type = NotificationType.Information, TimeSpan? expiration = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Show(title, message, type, expiration));
            return;
        }

        if (_notificationManager == null) return;

        var notification = new Notification(
            title,
            message,
            type,
            expiration ?? TimeSpan.FromSeconds(5));

        _notificationManager.Show(notification);
    }

    public void Success(string message) => Show("SUCCESS", message, NotificationType.Success);
    public void Error(string message) => Show("ERROR", message, NotificationType.Error);
    public void Info(string message) => Show("INFO", message, NotificationType.Information);
    public void Warning(string message) => Show("WARNING", message, NotificationType.Warning);
}