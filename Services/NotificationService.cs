using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using System;
using System.IO;
using System.Reflection;

namespace Yellowcake.Services;

public class NotificationService
{
    private static NotificationService? _instance;
    public static NotificationService Instance => _instance ??= new NotificationService();

    private IManagedNotificationManager? _notificationManager;

    public void Initialize(Visual visual)
    {
        var topLevel = TopLevel.GetTopLevel(visual);
        if (topLevel == null) return;

        _notificationManager = new WindowNotificationManager(topLevel)
        {
            Position = NotificationPosition.BottomCenter,
            MaxItems = 3
        };
    }

    public void Show(string title, string message, NotificationType type = NotificationType.Information, TimeSpan? expiration = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Show(title, message, type, expiration));
            return;
        }

        _notificationManager?.Show(new Notification(
            title,
            message,
            type,
            expiration ?? TimeSpan.FromSeconds(4)));
    }

    public void Success(string message) => Show("Success", message, NotificationType.Success);
    public void Error(string message) => Show("Error", message, NotificationType.Error);
    public void Info(string message) => Show("Information", message, NotificationType.Information);
    public void Warning(string message) => Show("Warning", message, NotificationType.Warning);
}