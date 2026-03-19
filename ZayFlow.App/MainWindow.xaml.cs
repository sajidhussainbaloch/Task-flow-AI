using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ZayFlow.App.Services;
using ZayFlow.App.ViewModels;

namespace ZayFlow.App;

/// <summary>
/// Main application window — Premium UI with sidebar navigation.
/// </summary>
public partial class MainWindow : Window
{
    private readonly WindowService _windowService;
    private readonly NotificationService _notificationService;
    private readonly IAppPreferencesService _preferencesService;
    private readonly ThemeService _themeService;
    public NavigationViewModel ViewModel { get; }

    public MainWindow(NavigationViewModel viewModel, WindowService windowService, NotificationService notificationService, IAppPreferencesService preferencesService, ThemeService themeService)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _windowService = windowService ?? throw new ArgumentNullException(nameof(windowService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        
        DataContext = ViewModel;
        InitializeComponent();

        _windowService.Initialize(this, minimizeToTray: _preferencesService.Get().MinimizeToTray);

        // Fix maximize to respect taskbar
        this.SourceInitialized += (s, e) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
            EnableAcrylicBlur(handle);
        };

        // Handle state changes for premium chrome
        this.StateChanged += OnWindowStateChanged;

        // Center on first load
        this.Loaded += (s, e) => _windowService.RestoreLastPosition();

        // Re-apply acrylic blur when theme changes
        _themeService.ThemeChanged += RefreshAcrylicBlur;
    }

    // ══════════ Win32 Acrylic Blur (Glass Effect) ══════════

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
        public uint GradientColor;  // AABBGGRR format
        public int AnimationId;
    }

    private enum WindowCompositionAttribute
    {
        WCA_ACCENT_POLICY = 19
    }

    private void EnableAcrylicBlur(IntPtr hwnd)
    {
        try
        {
            // Determine tint color based on current theme (dark = deep navy, light = frosted white)
            var isDark = _themeService.IsDark;
            // AABBGGRR — we want semi-transparent tint matching the window background
            // Dark: #0F0F14 at ~82% opacity → AA=D1, BB=14, GG=0F, RR=0F → 0xD1140F0F
            // Light: #FAFBFC at ~78% opacity → AA=C7, BB=FC, GG=FB, RR=FA → 0xC7FCFBFA
            uint tintColor = isDark ? 0xD1140F0F : 0xC7FCFBFA;

            var accent = new AccentPolicy
            {
                AccentState = 4,    // ACCENT_ENABLE_ACRYLICBLURBEHIND
                AccentFlags = 2,    // ACCENT_FLAG_DRAW_ALL
                GradientColor = tintColor,
                AnimationId = 0
            };

            var accentSize = Marshal.SizeOf(accent);
            var accentPtr = Marshal.AllocHGlobal(accentSize);
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                Data = accentPtr,
                SizeOfData = accentSize
            };

            SetWindowCompositionAttribute(hwnd, ref data);
            Marshal.FreeHGlobal(accentPtr);
        }
        catch
        {
            // Fallback: acrylic not supported on this OS version — solid background remains
        }
    }

    /// <summary>
    /// Re-apply acrylic blur when theme changes (called from theme toggle).
    /// </summary>
    public void RefreshAcrylicBlur()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            EnableAcrylicBlur(hwnd);
    }

    // ══════════ Window State → Chrome Adaptation ══════════
    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            MainBorder.Margin = new Thickness(0);
            MainBorder.CornerRadius = new CornerRadius(0);
            MainBorder.Effect = null;
            TitleBarBorder.CornerRadius = new CornerRadius(0);
            SidebarBorder.CornerRadius = new CornerRadius(0);
            MaximizeBtn.Content = "❐";
        }
        else
        {
            MainBorder.Margin = new Thickness(8);
            MainBorder.CornerRadius = new CornerRadius(14);
            MainBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = System.Windows.Media.Colors.Black,
                Opacity = 0.50,
                BlurRadius = 28,
                ShadowDepth = 0,
                Direction = 270
            };
            TitleBarBorder.CornerRadius = new CornerRadius(14, 14, 0, 0);
            SidebarBorder.CornerRadius = new CornerRadius(0, 0, 0, 14);
            MaximizeBtn.Content = "□";
        }
    }

    /// <summary>
    /// Hide to tray instead of closing (unless app is shutting down).
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        _windowService.MinimizeWindow();
        base.OnClosing(e);
    }

    // ══════════ Win32: Fix maximize to respect taskbar ══════════

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public System.Drawing.Point ptReserved;
        public System.Drawing.Point ptMaxSize;
        public System.Drawing.Point ptMaxPosition;
        public System.Drawing.Point ptMinTrackSize;
        public System.Drawing.Point ptMaxTrackSize;
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // WM_GETMINMAXINFO = 0x0024
        if (msg == 0x0024)
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            var monitor = MonitorFromWindow(hwnd, 0x00000002); // MONITOR_DEFAULTTONEAREST
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref mi))
            {
                var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                mmi.ptMaxPosition = new System.Drawing.Point(mi.rcWork.Left - mi.rcMonitor.Left,
                                                              mi.rcWork.Top - mi.rcMonitor.Top);
                mmi.ptMaxSize = new System.Drawing.Point(mi.rcWork.Right - mi.rcWork.Left,
                                                          mi.rcWork.Bottom - mi.rcWork.Top);
                Marshal.StructureToPtr(mmi, lParam, true);
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    // ══════════ Title Bar Drag ══════════
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            MaximizeButton_Click(sender, new RoutedEventArgs());
        else
            DragMove();
    }

    // ══════════ Window Controls ══════════
    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        _windowService.MinimizeWindow();
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _notificationService.ShowInfo("TaskFlow AI is running in the background");
        _windowService.MinimizeWindow();
    }

    // ══════════ Navigation Click ══════════
    private void NavItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string key)
        {
            ViewModel.NavigateCommand.Execute(key);
        }
    }
}

