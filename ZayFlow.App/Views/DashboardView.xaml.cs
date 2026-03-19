using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ZayFlow.App.ViewModels;

namespace ZayFlow.App.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private void QuickAction_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && DataContext is DashboardViewModel vm)
        {
            vm.QuickActionCommand.Execute(fe.Tag?.ToString());
        }
    }
}
