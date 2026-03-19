using System;
using System.Windows;
using System.Windows.Media.Animation;
using Microsoft.Extensions.Logging;

namespace ZayFlow.App.Services;

/// <summary>
/// Manages window behavior, animations, and lifecycle.
/// </summary>
public class WindowService
{
    private readonly ILogger<WindowService> _logger;
    private MainWindow? _mainWindow;
    private bool _minimizeToTray = true;

    public WindowService(ILogger<WindowService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Initialize(MainWindow mainWindow, bool minimizeToTray = true)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        _minimizeToTray = minimizeToTray;

        // Smooth fade-in on first load
        mainWindow.Loaded += (s, e) => AnimateWindowEntry();
    }

    /// <summary>
    /// Smooth fade-in animation when window opens.
    /// </summary>
    private void AnimateWindowEntry()
    {
        if (_mainWindow is null) return;

        // Start transparent
        _mainWindow.Opacity = 0;
        
        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        _mainWindow.BeginAnimation(UIElement.OpacityProperty, fadeIn);

        _logger.LogInformation("Window fade-in animation started");
    }

    /// <summary>
    /// Fade out and minimize to tray (or taskbar if disabled).
    /// </summary>
    public void MinimizeWindow()
    {
        if (_mainWindow is null) return;

        if (_minimizeToTray)
        {
            AnimateWindowExit(() =>
            {
                _mainWindow.Hide();
                _mainWindow.WindowState = WindowState.Minimized;
            });
        }
        else
        {
            _mainWindow.WindowState = WindowState.Minimized;
        }
    }

    /// <summary>
    /// Restore window with fade-in and center on screen.
    /// </summary>
    public void RestoreWindow()
    {
        if (_mainWindow is null) return;

        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Opacity = 0;
        _mainWindow.Show();

        // Center on screen
        var wa = SystemParameters.WorkArea;
        _mainWindow.Left = wa.Left + (wa.Width - _mainWindow.ActualWidth) / 2;
        _mainWindow.Top = wa.Top + (wa.Height - _mainWindow.ActualHeight) / 2;

        // Fade in
        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        _mainWindow.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
        _mainWindow.Activate();
        _mainWindow.Focus();

        _logger.LogInformation("Window restored with fade-in animation");
    }

    /// <summary>
    /// Animate smooth exit (used before hiding to tray).
    /// </summary>
    private void AnimateWindowExit(Action onComplete)
    {
        if (_mainWindow is null) return;

        var fadeOut = new DoubleAnimation
        {
            From = _mainWindow.Opacity,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        fadeOut.Completed += (s, e) => onComplete();
        _mainWindow.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    /// <summary>
    /// Get last saved window position from settings (for future enhancement).
    /// </summary>
    public void RestoreLastPosition()
    {
        if (_mainWindow is null) return;

        // For now, just center on screen
        var wa = SystemParameters.WorkArea;
        _mainWindow.Left = wa.Left + (wa.Width - _mainWindow.Width) / 2;
        _mainWindow.Top = wa.Top + (wa.Height - _mainWindow.Height) / 2;
    }

    public void SetMinimizeToTray(bool minimize)
    {
        _minimizeToTray = minimize;
        _logger.LogInformation($"Minimize to tray set to: {minimize}");
    }

    public bool GetMinimizeToTray() => _minimizeToTray;
}
