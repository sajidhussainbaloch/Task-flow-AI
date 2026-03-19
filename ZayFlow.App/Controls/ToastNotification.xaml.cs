using System.Windows;
using System.Windows.Controls;
using ZayFlow.App.Services;

namespace ZayFlow.App.Controls;

public partial class ToastNotification : UserControl
{
    public ToastNotification()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is NotificationItem notification)
        {
            // Trigger parent itemscontrol to remove via service
            var parent = Parent;
            while (parent != null && parent is not Window)
            {
                parent = (parent as FrameworkElement)?.Parent;
            }
        }
    }
}
