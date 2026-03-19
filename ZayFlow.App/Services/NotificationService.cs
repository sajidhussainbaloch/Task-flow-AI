using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace ZayFlow.App.Services;

public enum NotificationType
{
    Success,
    Warning,
    Error,
    Info
}

public class NotificationItem
{
    public string Id { get; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool IsVisible { get; set; } = true;

    public string Icon => Type switch
    {
        NotificationType.Success => "✓",
        NotificationType.Warning => "⚠",
        NotificationType.Error => "✕",
        NotificationType.Info => "ℹ",
        _ => "ℹ"
    };
}

public class NotificationService
{
    private readonly DispatcherTimer _cleanupTimer;

    public ObservableCollection<NotificationItem> Notifications { get; } = new();

    public NotificationService()
    {
        _cleanupTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _cleanupTimer.Tick += CleanupExpired;
        _cleanupTimer.Start();
    }

    public void ShowSuccess(string title, string message = "")
        => Show(title, message, NotificationType.Success);

    public void ShowWarning(string title, string message = "")
        => Show(title, message, NotificationType.Warning);

    public void ShowError(string title, string message = "")
        => Show(title, message, NotificationType.Error);

    public void ShowInfo(string title, string message = "")
        => Show(title, message, NotificationType.Info);

    public void Show(string title, string message, NotificationType type)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var notification = new NotificationItem
            {
                Title = title,
                Message = message,
                Type = type
            };
            Notifications.Insert(0, notification);

            // Limit to 5 visible notifications
            while (Notifications.Count > 5)
                Notifications.RemoveAt(Notifications.Count - 1);
        });
    }

    public void Dismiss(string id)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            for (int i = Notifications.Count - 1; i >= 0; i--)
            {
                if (Notifications[i].Id == id)
                {
                    Notifications.RemoveAt(i);
                    break;
                }
            }
        });
    }

    private void CleanupExpired(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            for (int i = Notifications.Count - 1; i >= 0; i--)
            {
                if ((DateTime.Now - Notifications[i].Timestamp).TotalSeconds > 5)
                    Notifications.RemoveAt(i);
            }
        });
    }
}
