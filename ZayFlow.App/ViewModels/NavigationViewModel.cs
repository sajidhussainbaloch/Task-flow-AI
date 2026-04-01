using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Commands;
using ZayFlow.App.Services;
using ZayFlow.App.Services.AI;
using ZayFlow.App.Services.AI.Contracts;
using ZayFlow.App.Services.AI.Providers;
using ZayFlow.App.Services.Assistant;
using ZayFlow.App.Services.CodeGeneration.Agent;
using ZayFlow.App.Services.CodeGeneration.Checkpointing;
using ZayFlow.App.Services.CodeGeneration.Notes;
using ZayFlow.App.Views;
using ZayFlow.Backend.Contracts;

namespace ZayFlow.App.ViewModels;

public class NavigationItem : ViewModelBase
{
    private bool _isSelected;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public class NavigationViewModel : ViewModelBase
{
    private readonly ThemeService _themeService;
    private readonly NotificationService _notificationService;
    private readonly MainViewModel _mainViewModel;

    private ViewModelBase? _currentPage;
    private string _currentPageKey = "AIAssistant";
    private bool _isSidebarExpanded = true;
    private string _userPlan = "Free";
    private int _tokenUsed = 23;
    private int _tokenLimit = 100;
    private string _greeting = string.Empty;
    private bool _isDark = true;

    public NavigationViewModel(
        ThemeService themeService,
        NotificationService notificationService,
        WindowService windowService,
        IAppPreferencesService preferencesService,
        MainViewModel mainViewModel,
        IAIService aiService,
        IntentExecutionService executionService,
        CloudflareProvider cloudflareProvider,
        IFileIntelligenceService fileIntelligenceService,
        INotesService notesService,
        IInsightsService insightsService,
        IActionAuditService actionAuditService,
        ILocalBackendService backendService,
        DownloadManager downloadManager,
        IAssistantOrchestrator orchestrator,
        RetryOrchestrationService retryService,
        WorkflowNotesService workflowNotes,
        WorkflowCheckpointService checkpointService)
    {
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
        var _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        var _executionService = executionService ?? throw new ArgumentNullException(nameof(executionService));
        var configuredCloudflareProvider = cloudflareProvider ?? throw new ArgumentNullException(nameof(cloudflareProvider));

        // Initialize Cloudflare from preferences
        var preferences = preferencesService.Get();
        if (!string.IsNullOrWhiteSpace(preferences.CloudflareApiToken)
            && !string.IsNullOrWhiteSpace(preferences.CloudflareAccountId))
        {
            configuredCloudflareProvider.Initialize($"{preferences.CloudflareApiToken}|{preferences.CloudflareAccountId}|{preferences.PreferredChatModel}");
        }

        _aiService.SwitchProvider("Cloudflare");

        _isDark = _themeService.IsDark;
        _themeService.ThemeChanged += () =>
        {
            IsDark = _themeService.IsDark;
        };

        UpdateGreeting();

        // Initialize page ViewModels
        DashboardVM = new DashboardViewModel(_notificationService, _mainViewModel);
        AutomationVM = new AutomationViewModel(_mainViewModel, _notificationService);
        AIAssistantVM = new AIAssistantViewModel(_aiService, _executionService, backendService, preferencesService, orchestrator, retryService, workflowNotes, checkpointService);
        HistoryVM = new HistoryViewModel(preferencesService);

        // When a history session is clicked, navigate to AI chat and load it
        HistoryVM.SessionSelected += (_, entry) =>
        {
            if (!string.IsNullOrEmpty(entry.Id))
            {
                AIAssistantVM.LoadSessionById(entry.Id);
                OnNavigate("AIAssistant");
            }
        };
        FileIntelligenceVM = new FileIntelligenceViewModel(fileIntelligenceService, _notificationService, preferencesService);
        NotesVM = new NotesViewModel(notesService, _aiService, _notificationService);
        ProductivityToolsVM = new ProductivityToolsViewModel(_aiService, _notificationService);
        InsightsVM = new InsightsViewModel(insightsService, _notificationService);
        SettingsVM = new SettingsViewModel(_themeService, _notificationService, windowService, preferencesService, _aiService, configuredCloudflareProvider, actionAuditService, null);
        AccountVM = new AccountViewModel(_notificationService, preferencesService);
        DownloadsVM = new DownloadsViewModel(downloadManager);

        // Default to ZayFlow AI (main chat tab)
        _currentPage = AIAssistantVM;

        // ChatGPT-style: only Settings in the nav items (sidebar shows chat history inline)
        NavigationItems = new ObservableCollection<NavigationItem>
        {
            new() { Name = "ZayFlow AI", Icon = "\uE734", Key = "AIAssistant", IsSelected = true },
            new() { Name = "Settings", Icon = "\uE713", Key = "Settings" },
        };

        NavigateCommand = new RelayCommand(OnNavigate);
        ToggleSidebarCommand = new RelayCommand(_ => IsSidebarExpanded = !IsSidebarExpanded);
        ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());
        OpenSettingsCommand = new RelayCommand(_ => OnNavigate("Settings"));
        DismissNotificationCommand = new RelayCommand(o => _notificationService.Dismiss(o?.ToString() ?? ""));

        // Sync tokens from MainViewModel
        _tokenUsed = _aiService.TokensUsedToday;
        _tokenLimit = _aiService.DailyTokenLimit;
    }

    // Page ViewModels
    public DashboardViewModel DashboardVM { get; }
    public AutomationViewModel AutomationVM { get; }
    public AIAssistantViewModel AIAssistantVM { get; }
    public HistoryViewModel HistoryVM { get; }
    public FileIntelligenceViewModel FileIntelligenceVM { get; }
    public NotesViewModel NotesVM { get; }
    public ProductivityToolsViewModel ProductivityToolsVM { get; }
    public InsightsViewModel InsightsVM { get; }
    public SettingsViewModel SettingsVM { get; }
    public AccountViewModel AccountVM { get; }
    public DownloadsViewModel DownloadsVM { get; }

    public ObservableCollection<NavigationItem> NavigationItems { get; }
    public ObservableCollection<NotificationItem> Notifications => _notificationService.Notifications;

    // Commands
    public ICommand NavigateCommand { get; }
    public ICommand ToggleSidebarCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand DismissNotificationCommand { get; }

    public ViewModelBase? CurrentPage
    {
        get => _currentPage;
        set => SetProperty(ref _currentPage, value);
    }

    public string CurrentPageKey
    {
        get => _currentPageKey;
        set => SetProperty(ref _currentPageKey, value);
    }

    public bool IsSidebarExpanded
    {
        get => _isSidebarExpanded;
        set => SetProperty(ref _isSidebarExpanded, value);
    }

    public string UserPlan
    {
        get => _userPlan;
        set => SetProperty(ref _userPlan, value);
    }

    public int TokenUsed
    {
        get => _tokenUsed;
        set => SetProperty(ref _tokenUsed, value);
    }

    public int TokenLimit
    {
        get => _tokenLimit;
        set => SetProperty(ref _tokenLimit, value);
    }

    public string TokenText => $"{TokenUsed}/{TokenLimit}";

    public double TokenPercent => TokenLimit > 0 ? (double)TokenUsed / TokenLimit * 100 : 0;

    public string Greeting
    {
        get => _greeting;
        set => SetProperty(ref _greeting, value);
    }

    public bool IsDark
    {
        get => _isDark;
        set => SetProperty(ref _isDark, value);
    }

    public string ThemeIcon => IsDark ? "\uE706" : "\uE708";

    private void OnNavigate(object? parameter)
    {
        var key = parameter?.ToString() ?? "Dashboard";
        CurrentPageKey = key;

        foreach (var item in NavigationItems)
            item.IsSelected = item.Key == key;

        CurrentPage = key switch
        {
            "Dashboard" => DashboardVM,
            "AIAssistant" => AIAssistantVM,
            "History" => HistoryVM,
            "FileIntelligence" => FileIntelligenceVM,
            "Automation" => AutomationVM,
            "Notes" => NotesVM,
            "ProductivityTools" => ProductivityToolsVM,
            "Insights" => InsightsVM,
            "Settings" => SettingsVM,
            "Downloads" => DownloadsVM,
            "Account" => AccountVM,
            _ => AIAssistantVM
        };
    }

    private void ToggleTheme()
    {
        _themeService.CurrentThemeSetting = _themeService.IsDark ? AppTheme.Light : AppTheme.Dark;
    }

    private void UpdateGreeting()
    {
        var hour = DateTime.Now.Hour;
        Greeting = hour switch
        {
            < 12 => "Good Morning",
            < 17 => "Good Afternoon",
            < 21 => "Good Evening",
            _ => "Good Night"
        };
    }
}
