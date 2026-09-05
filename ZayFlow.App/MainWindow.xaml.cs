using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ZayFlow.App.Services;
using ZayFlow.App.ViewModels;

namespace ZayFlow.App;

public partial class MainWindow : Window
{
    private readonly WindowService _windowService;
    private readonly NotificationService _notificationService;
    private readonly IAppPreferencesService _preferencesService;
    private readonly ThemeService _themeService;
    private bool _isPseudoMaximized;
    private bool _isHandlingStateChange;
    private Rect _restoreBounds;

    public NavigationViewModel ViewModel { get; }

    public MainWindow(
        NavigationViewModel viewModel,
        WindowService windowService,
        NotificationService notificationService,
        IAppPreferencesService preferencesService,
        ThemeService themeService)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _windowService = windowService ?? throw new ArgumentNullException(nameof(windowService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));

        DataContext = ViewModel;
        InitializeComponent();

        _windowService.Initialize(this, minimizeToTray: _preferencesService.Get().MinimizeToTray);

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
            ApplyBackdrop(handle);
        };

        StateChanged += OnWindowStateChanged;
        Loaded += (_, _) => _windowService.RestoreLastPosition();
        _themeService.ThemeChanged += RefreshBackdrop;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    private enum WindowCompositionAttribute
    {
        WcaAccentPolicy = 19
    }

    private void ApplyBackdrop(IntPtr hwnd)
    {
        try
        {
            const int DwmwaUseImmersiveDarkMode = 20;
            const int DwmwaSystemBackdropType = 38;
            const int DwmsbtMainWindow = 2;
            const int DwmsbtTransientWindow = 3;

            var darkMode = _themeService.IsDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));

            var backdrop = Environment.OSVersion.Version.Build >= 22523 ? DwmsbtMainWindow : DwmsbtTransientWindow;
            var result = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
            if (result != 0)
            {
                EnableAcrylicFallback(hwnd);
            }
        }
        catch
        {
            EnableAcrylicFallback(hwnd);
        }
    }

    private void EnableAcrylicFallback(IntPtr hwnd)
    {
        try
        {
            var tintColor = _themeService.IsDark ? 0xD1140F0F : 0xC7FCFBFA;
            var accent = new AccentPolicy
            {
                AccentState = 4,
                AccentFlags = 2,
                GradientColor = tintColor,
                AnimationId = 0
            };

            var accentSize = Marshal.SizeOf(accent);
            var accentPtr = Marshal.AllocHGlobal(accentSize);
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttribute.WcaAccentPolicy,
                Data = accentPtr,
                SizeOfData = accentSize
            };

            SetWindowCompositionAttribute(hwnd, ref data);
            Marshal.FreeHGlobal(accentPtr);
        }
        catch
        {
        }
    }

    public void RefreshBackdrop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            ApplyBackdrop(hwnd);
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (_isHandlingStateChange || WindowState == WindowState.Minimized)
        {
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            _isHandlingStateChange = true;
            WindowState = WindowState.Normal;
            ToggleMaximizeRestore();
            _isHandlingStateChange = false;
        }
        else
        {
            ApplyWindowChrome(false);
            _isPseudoMaximized = false;
        }
    }

    private void ApplyWindowChrome(bool isMaximized)
    {
        if (isMaximized)
        {
            MainBorder.Margin = new Thickness(0);
            MainBorder.CornerRadius = new CornerRadius(0);
            MainBorder.Effect = null;
            TitleBarBorder.CornerRadius = new CornerRadius(0);
            SidebarBorder.CornerRadius = new CornerRadius(0);
            MaximizeBtn.Content = "[]";
        }
        else
        {
            MainBorder.Margin = new Thickness(8);
            MainBorder.CornerRadius = new CornerRadius(14);
            MainBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = System.Windows.Media.Colors.Black,
                Opacity = 0.48,
                BlurRadius = 28,
                ShadowDepth = 0,
                Direction = 270
            };
            TitleBarBorder.CornerRadius = new CornerRadius(14, 14, 0, 0);
            SidebarBorder.CornerRadius = new CornerRadius(0, 0, 0, 14);
            MaximizeBtn.Content = "O";
        }
    }

    private Rect GetCurrentMonitorWorkArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var info = new MonitorInfo { cbSize = Marshal.SizeOf(typeof(MonitorInfo)) };
        var monitor = MonitorFromWindow(handle, 0x00000002);

        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
        {
            return new Rect(
                info.rcWork.Left,
                info.rcWork.Top,
                info.rcWork.Right - info.rcWork.Left,
                info.rcWork.Bottom - info.rcWork.Top);
        }

        return SystemParameters.WorkArea;
    }

    private void ToggleMaximizeRestore()
    {
        if (!_isPseudoMaximized)
        {
            _restoreBounds = new Rect(Left, Top, Width, Height);
            var workArea = GetCurrentMonitorWorkArea();
            WindowState = WindowState.Normal;
            Left = workArea.Left;
            Top = workArea.Top;
            Width = workArea.Width;
            Height = workArea.Height;
            _isPseudoMaximized = true;
            ApplyWindowChrome(true);
            return;
        }

        WindowState = WindowState.Normal;
        Left = _restoreBounds.Left;
        Top = _restoreBounds.Top;
        Width = _restoreBounds.Width;
        Height = _restoreBounds.Height;
        _isPseudoMaximized = false;
        ApplyWindowChrome(false);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        _windowService.MinimizeWindow();
        base.OnClosing(e);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public RectInt rcMonitor;
        public RectInt rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectInt
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public System.Drawing.Point ptReserved;
        public System.Drawing.Point ptMaxSize;
        public System.Drawing.Point ptMaxPosition;
        public System.Drawing.Point ptMinTrackSize;
        public System.Drawing.Point ptMaxTrackSize;
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmGetMinMaxInfo = 0x0024;
        if (msg == WmGetMinMaxInfo)
        {
            var info = new MonitorInfo { cbSize = Marshal.SizeOf(typeof(MonitorInfo)) };
            var monitor = MonitorFromWindow(hwnd, 0x00000002);
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                var data = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                data.ptMaxPosition = new System.Drawing.Point(
                    info.rcWork.Left - info.rcMonitor.Left,
                    info.rcWork.Top - info.rcMonitor.Top);
                data.ptMaxSize = new System.Drawing.Point(
                    info.rcWork.Right - info.rcWork.Left,
                    info.rcWork.Bottom - info.rcWork.Top);
                Marshal.StructureToPtr(data, lParam, true);
            }

            handled = true;
        }

        return IntPtr.Zero;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MaximizeButton_Click(sender, new RoutedEventArgs());
            return;
        }

        if (_isPseudoMaximized)
        {
            ToggleMaximizeRestore();
        }

        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        _windowService.MinimizeWindow();
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _notificationService.ShowInfo("TaskFlow AI is running in the background");
        _windowService.MinimizeWindow();
    }

    private void NavItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string key)
        {
            ViewModel.NavigateCommand.Execute(key);
        }
    }

    private void ChatHistoryItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string chatId)
        {
            // Navigate to AI page first, then load the chat
            ViewModel.NavigateCommand.Execute("AIAssistant");
            ViewModel.AIAssistantVM.LoadChatCommand.Execute(chatId);
        }
    }
}
