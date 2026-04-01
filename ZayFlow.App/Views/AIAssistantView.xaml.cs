using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ZayFlow.App.Views;

public partial class AIAssistantView : UserControl
{
    public AIAssistantView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.AIAssistantViewModel vm)
        {
            ((INotifyCollectionChanged)vm.Messages).CollectionChanged += OnMessagesChanged;
            vm.ScrollRequested += ScrollToBottom;
            ScrollToBottom();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.AIAssistantViewModel vm)
        {
            ((INotifyCollectionChanged)vm.Messages).CollectionChanged -= OnMessagesChanged;
            vm.ScrollRequested -= ScrollToBottom;
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            ScrollToBottom();
        }
    }

    private void ScrollToBottom()
    {
        Dispatcher.InvokeAsync(() => ChatScrollViewer?.ScrollToEnd(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                // Shift+Enter: insert newline
                var tb = (TextBox)sender;
                var caretIndex = tb.CaretIndex;
                tb.Text = tb.Text.Insert(caretIndex, Environment.NewLine);
                tb.CaretIndex = caretIndex + Environment.NewLine.Length;
                e.Handled = true;
            }
            else
            {
                // Enter: send message
                if (DataContext is ViewModels.AIAssistantViewModel vm && vm.SendCommand.CanExecute(null))
                {
                    vm.SendCommand.Execute(null);
                }
                e.Handled = true;
            }
        }
    }

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Clipboard.ContainsImage())
        {
            if (DataContext is ViewModels.AIAssistantViewModel vm && vm.PasteImageCommand.CanExecute(null))
            {
                vm.PasteImageCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void Root_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = HasImageFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Root_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not ViewModels.AIAssistantViewModel vm)
        {
            return;
        }

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (files == null)
        {
            return;
        }

        foreach (var file in files.Where(IsImageFile))
        {
            vm.AddDroppedImage(file);
        }

        e.Handled = true;
    }

    private static bool HasImageFiles(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        return files != null && files.Any(IsImageFile);
    }

    private static bool IsImageFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }
}
