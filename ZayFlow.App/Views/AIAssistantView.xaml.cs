using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ZayFlow.App.Views;

public partial class AIAssistantView : UserControl
{
    public AIAssistantView()
    {
        InitializeComponent();
        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Subscribe to messages collection changes for auto-scroll
        if (DataContext is ViewModels.AIAssistantViewModel vm)
        {
            ((INotifyCollectionChanged)vm.Messages).CollectionChanged += OnMessagesChanged;
            // Initial scroll to bottom
            ScrollToBottom();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.AIAssistantViewModel vm)
        {
            ((INotifyCollectionChanged)vm.Messages).CollectionChanged -= OnMessagesChanged;
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Auto-scroll to bottom when new messages are added (ChatGPT behavior)
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            ScrollToBottom();
        }
    }

    private void ScrollToBottom()
    {
        Dispatcher.InvokeAsync(() =>
        {
            ChatScrollViewer?.ScrollToEnd();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (DataContext is ViewModels.AIAssistantViewModel vm && vm.SendCommand.CanExecute(null))
            {
                vm.SendCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void CopyMessage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string content)
        {
            try
            {
                Clipboard.SetText(content);
            }
            catch { /* clipboard may be locked */ }
        }
    }
}
