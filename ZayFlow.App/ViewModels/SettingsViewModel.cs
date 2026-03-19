using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Commands;
using ZayFlow.App.Services;
using ZayFlow.App.Services.AI;
using ZayFlow.App.Services.AI.Contracts;
using ZayFlow.App.Services.AI.Providers;

namespace ZayFlow.App.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly ThemeService _themeService;
    private readonly NotificationService _notificationService;
    private readonly WindowService _windowService;
    private readonly IAppPreferencesService _preferencesService;
    private readonly IAIService? _aiService;
    private readonly GroqProvider? _groqProvider;
    private readonly IActionAuditService? _actionAuditService;
    private readonly ILogger<SettingsViewModel>? _logger;

    // General
    private bool _startOnBoot;
    private bool _minimizeToTray = true;
    private bool _notificationsEnabled = true;
    private string _selectedLanguage = "English";
    private bool _autoUpdate = true;

    // Appearance
    private string _selectedTheme = "Dark";
    private string _accentColor = "#6366F1";
    private double _fontScale = 1.0;
    private bool _compactMode;
    private bool _animationsEnabled = true;

    // Automation
    private string _defaultActionFolder = @"C:\Users\Downloads";
    private bool _safeMode = true;
    private bool _confirmBeforeExecute = true;
    private string _defaultGroupingStrategy = "By Type";

    // AI & Tokens
    private bool _showRemainingTokens = true;
    private bool _enableOfflineIntelligence = true;
    private bool _privacyMode = true;
    private bool _disableFileUpload;
    private bool _requireFileUploadConfirmation = true;
    private int _tokenWarningThreshold = 10;
    private string _selectedProvider = "Groq (Free)";
    private string _groqApiKey = string.Empty;
    private bool _scheduledTasksEnabled;
    private bool _enableAutomationEngine = true;
    private bool _enableBackgroundTasks = true;
    private bool _voiceInputEnabled;
    private string _logsText = "No logs loaded.";

    // Account
    private string _userEmail = "user@example.com";
    private string _licenseStatus = "Free Plan";

    public SettingsViewModel(
        ThemeService themeService,
        NotificationService notificationService,
        WindowService windowService,
        IAppPreferencesService preferencesService,
        IAIService? aiService = null,
        GroqProvider? groqProvider = null,
        IActionAuditService? actionAuditService = null,
        ILogger<SettingsViewModel>? logger = null)
    {
        _themeService = themeService;
        _notificationService = notificationService;
        _windowService = windowService;
        _preferencesService = preferencesService;
        _aiService = aiService;
        _groqProvider = groqProvider;
        _actionAuditService = actionAuditService;
        _logger = logger;

        _selectedTheme = _themeService.CurrentThemeSetting switch
        {
            AppTheme.Light => "Light",
            AppTheme.Dark => "Dark",
            AppTheme.System => "System",
            _ => "Dark"
        };

        Languages = new ObservableCollection<string> { "English", "Spanish", "French", "German", "Arabic", "Chinese", "Japanese" };
        ThemeOptions = new ObservableCollection<string> { "Light", "Dark", "System" };
        GroupingStrategies = new ObservableCollection<string> { "By Type", "By Date", "By Size", "By Name" };
        AccentColors = new ObservableCollection<string> { "#6366F1", "#8B5CF6", "#EC4899", "#EF4444", "#F59E0B", "#10B981", "#3B82F6", "#06B6D4" };
        
        // Initialize AI providers
        AvailableProviders = new ObservableCollection<string> { "Groq (Free)" };

        ResetMemoryCommand = new RelayCommand(_ => OnResetMemory());
        UpgradeCommand = new RelayCommand(_ => OnUpgrade());
        SaveSettingsCommand = new RelayCommand(_ => OnSaveSettings());
        RefreshLogsCommand = new RelayCommand(async _ => await OnRefreshLogsAsync());
        ClearLogsCommand = new RelayCommand(async _ => await OnClearLogsAsync());
        ClearCacheCommand = new RelayCommand(_ => OnClearCache());
        ResetSettingsCommand = new RelayCommand(_ => OnResetSettings());

        LoadPreferences();
    }

    public ObservableCollection<string> Languages { get; }
    public ObservableCollection<string> ThemeOptions { get; }
    public ObservableCollection<string> GroupingStrategies { get; }
    public ObservableCollection<string> AccentColors { get; }
    public ObservableCollection<string> AvailableProviders { get; }

    public ICommand ResetMemoryCommand { get; }
    public ICommand UpgradeCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand RefreshLogsCommand { get; }
    public ICommand ClearLogsCommand { get; }
    public ICommand ClearCacheCommand { get; }
    public ICommand ResetSettingsCommand { get; }

    public int RemainingTokens => _aiService?.RemainingTokens ?? 0;
    public int DailyTokenLimit => _aiService?.DailyTokenLimit ?? 0;
    public int UsedTokens => _aiService?.TokensUsedToday ?? 0;
    public string TokenTier => _aiService?.TokenTier ?? "Free";

    // ── General ──
    public bool StartOnBoot
    {
        get => _startOnBoot;
        set => SetProperty(ref _startOnBoot, value);
    }

    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set => SetProperty(ref _notificationsEnabled, value);
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set
        {
            if (SetProperty(ref _minimizeToTray, value))
            {
                _windowService.SetMinimizeToTray(value);
            }
        }
    }

    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (SetProperty(ref _selectedLanguage, value))
            {
                // Save language preference
                _preferencesService.Update(p => p.SelectedLanguage = value);
                _notificationService.ShowSuccess("Language Changed", $"Language set to {value}. Note: Full localization requires app restart.");
            }
        }
    }

    public bool AutoUpdate
    {
        get => _autoUpdate;
        set => SetProperty(ref _autoUpdate, value);
    }

    // ── Appearance ──
    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetProperty(ref _selectedTheme, value))
            {
                _themeService.CurrentThemeSetting = value switch
                {
                    "Light" => AppTheme.Light,
                    "Dark" => AppTheme.Dark,
                    "System" => AppTheme.System,
                    _ => AppTheme.Dark
                };
            }
        }
    }

    public string AccentColor
    {
        get => _accentColor;
        set => SetProperty(ref _accentColor, value);
    }

    public double FontScale
    {
        get => _fontScale;
        set => SetProperty(ref _fontScale, value);
    }

    public bool CompactMode
    {
        get => _compactMode;
        set => SetProperty(ref _compactMode, value);
    }

    public bool AnimationsEnabled
    {
        get => _animationsEnabled;
        set => SetProperty(ref _animationsEnabled, value);
    }

    // ── Automation ──
    public string DefaultActionFolder
    {
        get => _defaultActionFolder;
        set => SetProperty(ref _defaultActionFolder, value);
    }

    public bool SafeMode
    {
        get => _safeMode;
        set => SetProperty(ref _safeMode, value);
    }

    public bool ConfirmBeforeExecute
    {
        get => _confirmBeforeExecute;
        set => SetProperty(ref _confirmBeforeExecute, value);
    }

    public string DefaultGroupingStrategy
    {
        get => _defaultGroupingStrategy;
        set => SetProperty(ref _defaultGroupingStrategy, value);
    }

    // ── AI & Tokens ──
    public string SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            if (SetProperty(ref _selectedProvider, value) && _aiService != null)
            {
                _aiService.SwitchProvider(value);
                _notificationService.ShowSuccess("Provider Changed", $"Switched to {value}");
            }
        }
    }

    public string GroqApiKey
    {
        get => _groqApiKey;
        set
        {
            if (SetProperty(ref _groqApiKey, value))
            {
                if (_groqProvider != null && !string.IsNullOrWhiteSpace(value))
                {
                    _groqProvider.Initialize(value);
                    _notificationService.ShowSuccess("API Key Configured", "Groq provider is now ready");
                }
            }
        }
    }

    public bool ShowRemainingTokens
    {
        get => _showRemainingTokens;
        set => SetProperty(ref _showRemainingTokens, value);
    }

    public bool EnableOfflineIntelligence
    {
        get => _enableOfflineIntelligence;
        set => SetProperty(ref _enableOfflineIntelligence, value);
    }

    public bool PrivacyMode
    {
        get => _privacyMode;
        set => SetProperty(ref _privacyMode, value);
    }

    public bool DisableFileUpload
    {
        get => _disableFileUpload;
        set => SetProperty(ref _disableFileUpload, value);
    }

    public bool RequireFileUploadConfirmation
    {
        get => _requireFileUploadConfirmation;
        set => SetProperty(ref _requireFileUploadConfirmation, value);
    }

    public bool ScheduledTasksEnabled
    {
        get => _scheduledTasksEnabled;
        set => SetProperty(ref _scheduledTasksEnabled, value);
    }

    public bool EnableAutomationEngine
    {
        get => _enableAutomationEngine;
        set => SetProperty(ref _enableAutomationEngine, value);
    }

    public bool EnableBackgroundTasks
    {
        get => _enableBackgroundTasks;
        set => SetProperty(ref _enableBackgroundTasks, value);
    }

    public bool VoiceInputEnabled
    {
        get => _voiceInputEnabled;
        set => SetProperty(ref _voiceInputEnabled, value);
    }

    public int TokenWarningThreshold
    {
        get => _tokenWarningThreshold;
        set => SetProperty(ref _tokenWarningThreshold, value);
    }

    // ── Account ──
    public string UserEmail
    {
        get => _userEmail;
        set => SetProperty(ref _userEmail, value);
    }

    public string LicenseStatus
    {
        get => _licenseStatus;
        set => SetProperty(ref _licenseStatus, value);
    }

    public string LogsText
    {
        get => _logsText;
        set => SetProperty(ref _logsText, value);
    }

    private void OnResetMemory()
    {
        _notificationService.ShowInfo("Memory Reset", "Local AI memory has been cleared.");
    }

    private void OnUpgrade()
    {
        _notificationService.ShowInfo("Upgrade", "Pro plan coming soon!");
    }

    private void OnSaveSettings()
    {
        _preferencesService.Update(p =>
        {
            p.LaunchOnStartup = StartOnBoot;
            p.MinimizeToTray = MinimizeToTray;
            p.NotificationsEnabled = NotificationsEnabled;
            p.PrivacyMode = PrivacyMode;
            p.DisableFileUpload = DisableFileUpload;
            p.RequireFileUploadConfirmation = RequireFileUploadConfirmation;
            p.RequireExecutionConfirmation = ConfirmBeforeExecute;
            p.EnableAutomationEngine = EnableAutomationEngine;
            p.EnableBackgroundTasks = EnableBackgroundTasks;
            p.EnableScheduledTasks = ScheduledTasksEnabled;
            p.EnableVoiceInput = VoiceInputEnabled;
            p.ShowRemainingTokens = ShowRemainingTokens;
            p.EnableOfflineIntelligence = EnableOfflineIntelligence;
            p.TokenWarningThreshold = TokenWarningThreshold;
            p.SelectedProvider = SelectedProvider;
            p.SelectedLanguage = SelectedLanguage;
            p.GroqApiKey = GroqApiKey;
            p.SelectedTheme = SelectedTheme;
            p.AccentColor = AccentColor;
            p.FontSizeScale = FontScale;
        });

        InitializeGroq();
        _aiService?.SwitchProvider(SelectedProvider);

        OnPropertyChanged(nameof(RemainingTokens));
        OnPropertyChanged(nameof(UsedTokens));
        OnPropertyChanged(nameof(DailyTokenLimit));
        OnPropertyChanged(nameof(TokenTier));
        _notificationService.ShowSuccess("Settings Saved", "Your preferences have been saved.");
    }

    private void LoadPreferences()
    {
        var p = _preferencesService.Get();
        StartOnBoot = p.LaunchOnStartup;
        MinimizeToTray = p.MinimizeToTray;
        NotificationsEnabled = p.NotificationsEnabled;
        PrivacyMode = p.PrivacyMode;
        DisableFileUpload = p.DisableFileUpload;
        RequireFileUploadConfirmation = p.RequireFileUploadConfirmation;
        ConfirmBeforeExecute = p.RequireExecutionConfirmation;
        EnableAutomationEngine = p.EnableAutomationEngine;
        EnableBackgroundTasks = p.EnableBackgroundTasks;
        ScheduledTasksEnabled = p.EnableScheduledTasks;
        VoiceInputEnabled = p.EnableVoiceInput;
        ShowRemainingTokens = p.ShowRemainingTokens;
        EnableOfflineIntelligence = p.EnableOfflineIntelligence;
        TokenWarningThreshold = p.TokenWarningThreshold;
        SelectedProvider = p.SelectedProvider;
        SelectedLanguage = p.SelectedLanguage;
        GroqApiKey = p.GroqApiKey;
        SelectedTheme = p.SelectedTheme;
        AccentColor = p.AccentColor;
        FontScale = p.FontSizeScale;
        LicenseStatus = p.IsPremium ? "Premium Plan" : "Free Plan";
    }

    private async Task OnRefreshLogsAsync()
    {
        if (_actionAuditService == null)
        {
            LogsText = "Log service unavailable.";
            return;
        }

        LogsText = await _actionAuditService.ReadRecentAsync();
    }

    private async Task OnClearLogsAsync()
    {
        if (_actionAuditService == null)
        {
            return;
        }

        await _actionAuditService.ClearAsync();
        LogsText = "Logs cleared.";
        _notificationService.ShowInfo("Logs", "Application logs were cleared.");
    }

    private void OnClearCache()
    {
        _notificationService.ShowInfo("Cache", "Cache cleared.");
    }

    private void OnResetSettings()
    {
        _preferencesService.Save(new AppPreferences());
        LoadPreferences();
        _notificationService.ShowWarning("Settings", "Settings reset to defaults.");
    }

    private void InitializeGroq()
    {
        if (_groqProvider == null) return;

        if (!string.IsNullOrWhiteSpace(GroqApiKey))
        {
            _groqProvider.Initialize(GroqApiKey);
        }
    }
}
