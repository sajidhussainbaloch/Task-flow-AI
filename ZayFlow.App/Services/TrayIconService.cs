using System.Drawing;
using System.IO;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Contracts;
using ZayFlow.App.ViewModels;

namespace ZayFlow.App.Services;

public class TrayIconService : IDisposable
{
    private readonly ILogger<TrayIconService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly NotificationService _notificationService;
    private readonly WindowService _windowService;
    private readonly IAIService _aiService;

    private TaskbarIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private NavigationViewModel? _viewModel;

    private System.Windows.Controls.MenuItem? _statusItem;

    private bool _disposed;

    public TrayIconService(ILogger<TrayIconService> logger, IServiceProvider serviceProvider,
                          NotificationService notificationService, WindowService windowService, IAIService aiService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _windowService = windowService ?? throw new ArgumentNullException(nameof(windowService));
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
    }

    public void AttachMainWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
    }

    public void Initialize()
    {
        try
        {
            Icon? appIcon = null;
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "TrayIcon.ico");
            if (File.Exists(iconPath))
            {
                appIcon = new Icon(iconPath);
            }

            _trayIcon = new TaskbarIcon
            {
                Icon = appIcon ?? SystemIcons.Application,
                ToolTipText = "TaskFlow AI"
            };

            _trayIcon.TrayLeftMouseDown += (_, _) => ShowWindow();
            _trayIcon.TrayMouseDoubleClick += (_, _) => ShowWindow();

            var menu = BuildContextMenu();
            menu.Opened += (_, _) => RefreshMenuState();
            _trayIcon.ContextMenu = menu;

            RefreshMenuState();
            _logger.LogInformation("Tray icon initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing tray icon");
            MessageBox.Show($"Tray initialization failed:\n{ex.Message}", "ZayFlow", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private System.Windows.Controls.ContextMenu BuildContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        _statusItem = new System.Windows.Controls.MenuItem { IsEnabled = false };

        menu.Items.Add(_statusItem);
        menu.Items.Add(new System.Windows.Controls.Separator());

        var showItem = new System.Windows.Controls.MenuItem { Header = "Open" };
        showItem.Click += (_, _) => ShowWindow();
        menu.Items.Add(showItem);

        var settingsItem = new System.Windows.Controls.MenuItem { Header = "Settings" };
        settingsItem.Click += (_, _) => ShowSettings();
        menu.Items.Add(settingsItem);

        var restartAiItem = new System.Windows.Controls.MenuItem { Header = "Restart AI" };
        restartAiItem.Click += (_, _) => RestartAi();
        menu.Items.Add(restartAiItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) =>
        {
            _mainWindow?.Close();
            Application.Current.Shutdown();
        };
        menu.Items.Add(exitItem);

        return menu;
    }

    private void RefreshMenuState()
    {
        if (_statusItem != null)
        {
            _statusItem.Header = "TaskFlow AI running";
        }
    }

    private void ShowAIAssistant()
    {
        ShowWindow();
        try
        {
            _viewModel ??= _serviceProvider.GetRequiredService<NavigationViewModel>();
            if (_viewModel.NavigateCommand.CanExecute("AIAssistant"))
            {
                _viewModel.NavigateCommand.Execute("AIAssistant");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to AI Assistant from tray");
        }
    }

    private void RestartAi()
    {
        _aiService.ClearHistory();
        _notificationService.ShowInfo("AI Restarted", "AI session and memory were reset.");
    }

    private void ShowSettings()
    {
        ShowWindow();
        try
        {
            _viewModel ??= _serviceProvider.GetRequiredService<NavigationViewModel>();
            if (_viewModel.NavigateCommand.CanExecute("Settings"))
            {
                _viewModel.NavigateCommand.Execute("Settings");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to Settings from tray");
        }
    }

    private void ShowWindow()
    {
        try
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_mainWindow == null)
                {
                    _mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                }

                try
                {
                    _windowService.RestoreWindow();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "WindowService restore failed; using direct restore fallback");
                    _mainWindow.WindowState = WindowState.Normal;
                    _mainWindow.Show();
                    _mainWindow.Activate();
                    _mainWindow.Focus();
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error showing main window");
            try
            {
                var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZayFlow");
                Directory.CreateDirectory(appData);
                var path = Path.Combine(appData, "tray-window-error.log");
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n---\n");
            }
            catch
            {
                // ignore logging failures
            }
            MessageBox.Show($"Failed to open window:\n{ex.Message}", "ZayFlow", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _trayIcon?.Dispose();
    }
}
