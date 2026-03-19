using System.Windows;
using System.IO;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZayFlow.Actions.Services;
using ZayFlow.App.Services;
using ZayFlow.App.Services.AI;
using ZayFlow.App.Services.AI.Contracts;
using ZayFlow.App.Services.AI.Handlers;
using ZayFlow.App.Services.AI.Providers;
using ZayFlow.App.ViewModels;
using ZayFlow.Backend.Services;
using ZayFlow.Core.Abstractions;
using ZayFlow.Core.Services;
using ZayFlow.Infrastructure.Services;
using ZayFlow.Planner.Services;

namespace ZayFlow.App;

/// <summary>
/// WPF Application entry point with DI composition root.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private TrayIconService? _trayIconService;
    private string? _lastCrashLogPath;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterGlobalExceptionHandlers();
        ConfigureServices();
        var provider = _serviceProvider ?? throw new InvalidOperationException("Service provider not initialized");

        var logger = provider.GetRequiredService<ILogger<App>>();

        try
        {
            // Initialize tray icon (app lives in tray)
            _trayIconService = provider.GetRequiredService<TrayIconService>();
            _trayIconService.Initialize();

            logger.LogInformation("ZayFlow launched — running in system tray");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fatal error during startup");
            MessageBox.Show($"Startup error: {ex.Message}", "ZayFlow", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            _lastCrashLogPath = WriteCrashLog("DispatcherUnhandledException", args.Exception);
            MessageBox.Show($"Unexpected error: {args.Exception.Message}\n\nCrash log: {_lastCrashLogPath}", "ZayFlow", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                _lastCrashLogPath = WriteCrashLog("UnhandledException", ex);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _lastCrashLogPath = WriteCrashLog("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    private string WriteCrashLog(string source, Exception ex)
    {
        var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZayFlow");
        Directory.CreateDirectory(logDirectory);

        var logPath = Path.Combine(logDirectory, "crash.log");
        var content =
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}" +
            $"Message: {ex.Message}{Environment.NewLine}" +
            $"Type: {ex.GetType().FullName}{Environment.NewLine}" +
            $"Stack:{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}" +
            new string('-', 80) + Environment.NewLine;

        File.AppendAllText(logPath, content);
        return logPath;
    }

    private void ConfigureServices()
    {
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(b =>
        {
            b.AddDebug();
            b.AddConsole();
            b.SetMinimumLevel(LogLevel.Information);
        });

        // Core automation services
        services.AddSingleton<IFileScanner, FileScanner>();
        services.AddSingleton<IPlanner, RuleBasedPlanner>();
        services.AddSingleton<IRiskAnalyzer, RiskAnalyzer>();
        services.AddSingleton<IPreviewService, PreviewService>();
        services.AddSingleton<IActionExecutor, ActionExecutor>();
        services.AddSingleton<IUndoManager, UndoManager>();
        services.AddSingleton<IAutomationService, AutomationService>();
        services.AddZayFlowLocalBackend();

        // Premium UI Services
        services.AddSingleton<ThemeService>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<WindowService>();
        services.AddSingleton<IAppPreferencesService, AppPreferencesService>();

        // AI Services
        services.AddSingleton<HttpClient>();  // Register HttpClient for AI providers
        services.AddSingleton<IFileActionService, FileActionService>();
        services.AddSingleton<ISystemActionService, SystemActionService>();
        services.AddSingleton<IActionAuditService, ActionAuditService>();
        services.AddSingleton<DocumentCreationService>();
        services.AddSingleton<DownloadManager>();
        services.AddSingleton<IntentExecutionService>();
        services.AddSingleton<IAIService, AIService>();

        // Handler services for new intents (61-91)
        services.AddSingleton<FileToolsHandler>();
        services.AddSingleton<MediaHandler>();
        services.AddSingleton<NetworkHandler>();
        services.AddSingleton<SystemPowerHandler>();
        services.AddSingleton<DevToolsHandler>();
        services.AddSingleton<BackgroundAutomationHandler>();
        services.AddSingleton<WorkflowHandler>();
        services.AddSingleton<HubHandler>();
        services.AddSingleton<IFileIntelligenceService, FileIntelligenceService>();
        services.AddSingleton<INotesService, NotesService>();
        services.AddSingleton<IInsightsService, InsightsService>();
        // Register AI providers
        services.AddSingleton<GroqProvider>();
        services.AddSingleton<IAIProvider>(sp => sp.GetRequiredService<GroqProvider>());

        // ViewModels - use Singleton so the same instance is reused
        services.AddSingleton<MainViewModel>();  // Keep for legacy/backend integration
        services.AddSingleton<NavigationViewModel>();  // New main shell VM
        services.AddSingleton<AccountViewModel>();  // Account/About tab
        
        // UI Components
        services.AddSingleton<MainWindow>();
        services.AddSingleton<TrayIconService>();

        _serviceProvider = services.BuildServiceProvider();

        // Wire up late-bound dependencies (avoids circular DI)
        var intentService = _serviceProvider.GetRequiredService<IntentExecutionService>();
        var aiService = _serviceProvider.GetRequiredService<IAIService>();
        intentService.SetAIService(aiService);
        intentService.SetNotificationService(_serviceProvider.GetRequiredService<NotificationService>());
        intentService.SetHandlers(
            _serviceProvider.GetRequiredService<FileToolsHandler>(),
            _serviceProvider.GetRequiredService<MediaHandler>(),
            _serviceProvider.GetRequiredService<NetworkHandler>(),
            _serviceProvider.GetRequiredService<SystemPowerHandler>(),
            _serviceProvider.GetRequiredService<DevToolsHandler>(),
            _serviceProvider.GetRequiredService<BackgroundAutomationHandler>(),
            _serviceProvider.GetRequiredService<WorkflowHandler>(),
            _serviceProvider.GetRequiredService<HubHandler>());

        // Initialize theme service
        var themeService = _serviceProvider.GetRequiredService<ThemeService>();
        themeService.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIconService?.Dispose();
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Gets a service from the DI container.
    /// </summary>
    public T GetService<T>() where T : class
        => _serviceProvider?.GetRequiredService<T>()
           ?? throw new InvalidOperationException("Service provider not initialized");
}
