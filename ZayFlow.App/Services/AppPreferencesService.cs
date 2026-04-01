using System.IO;
using System.Text.Json;
using ZayFlow.App.Services.AI.Providers;

namespace ZayFlow.App.Services;

public sealed class AppPreferences
{
    public bool LaunchOnStartup { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    public string SelectedLanguage { get; set; } = "English";
    public bool PrivacyMode { get; set; } = true;
    public bool DisableFileUpload { get; set; } = false;
    public bool RequireFileUploadConfirmation { get; set; } = true;
    public bool RequireExecutionConfirmation { get; set; } = true;
    public bool EnableAutomationEngine { get; set; } = true;
    public bool EnableBackgroundTasks { get; set; } = true;
    public bool EnableScheduledTasks { get; set; } = false;
    public bool EnableVoiceInput { get; set; } = false;
    public bool ShowRemainingTokens { get; set; } = true;
    public bool EnableOfflineIntelligence { get; set; } = true;
    public int TokenWarningThreshold { get; set; } = 10;
    public string SelectedProvider { get; set; } = "Cloudflare";
    public string CloudflareApiToken { get; set; } = string.Empty;
    public string CloudflareAccountId { get; set; } = string.Empty;
    public string SelectedTheme { get; set; } = "Dark";
    public string AccentColor { get; set; } = "#6366F1";
    public double FontSizeScale { get; set; } = 1.0;
    public bool IsPremium { get; set; } = false;
    public int FreeDailyLimit { get; set; } = 1000;
    public int PremiumDailyLimit { get; set; } = 1000;
    public DateOnly TokenWindowDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public int TokensUsedToday { get; set; } = 0;
    public List<ChatSessionHistoryRecord> ChatSessions { get; set; } = new();
    public List<CommandUsageRecord> CommandUsages { get; set; } = new();
    public List<SavedShortcutRecord> SavedShortcuts { get; set; } = new();
    public bool HasCompletedFirstAIAssistantUse { get; set; } = false;
    public bool HasDismissedActionCards { get; set; } = false;
    public bool IsAutoModeEnabled { get; set; } = false;
    public string DefaultWorkspaceRoot { get; set; } = string.Empty;
    public bool EnableImagePaste { get; set; } = true;
    public bool EnableStreamingResponses { get; set; } = true;
    public bool UseLocalOcrFirst { get; set; } = true;
    public string PreferredChatModel { get; set; } = CloudflareProvider.DefaultChatModel;
    public string PreferredCodingModel { get; set; } = CloudflareProvider.DefaultCodeModel;
    public string PreferredVisionModel { get; set; } = CloudflareProvider.DefaultVisionModel;
    public bool EnableArtifactPane { get; set; } = true;
}

public sealed class ChatSessionHistoryRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "New chat";
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    public string Preview { get; set; } = string.Empty;
    public List<ChatMessageRecord> Messages { get; set; } = new();
}

public sealed class ChatMessageRecord
{
    public string Content { get; set; } = string.Empty;
    public bool IsUser { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string MessageKind { get; set; } = "chat";
    public List<string> AttachmentIds { get; set; } = new();
    public List<string> ArtifactIds { get; set; } = new();
    public string ToolTraceSummary { get; set; } = string.Empty;
    public string WorkspaceRoot { get; set; } = string.Empty;
}

public sealed class CommandUsageRecord
{
    public string CommandText { get; set; } = string.Empty;
    public int UseCount { get; set; } = 0;
    public DateTime LastUsed { get; set; } = DateTime.Now;
}

public sealed class SavedShortcutRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string CommandText { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastUsed { get; set; } = DateTime.Now;
}

public interface IAppPreferencesService
{
    AppPreferences Get();
    void Save(AppPreferences preferences);
    void Update(Action<AppPreferences> apply);
}

public sealed class AppPreferencesService : IAppPreferencesService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;
    private readonly object _sync = new();
    private AppPreferences? _cached;

    public AppPreferencesService()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZayFlow");
        Directory.CreateDirectory(appData);
        _settingsPath = Path.Combine(appData, "preferences.json");
    }

    public AppPreferences Get()
    {
        lock (_sync)
        {
            _cached ??= LoadFromDisk();
            NormalizeTokenWindow(_cached);
            return Clone(_cached);
        }
    }

    public void Save(AppPreferences preferences)
    {
        lock (_sync)
        {
            NormalizeTokenWindow(preferences);
            _cached = Clone(preferences);
            var json = JsonSerializer.Serialize(_cached, JsonOptions);
            File.WriteAllText(_settingsPath, json);
        }
    }

    public void Update(Action<AppPreferences> apply)
    {
        lock (_sync)
        {
            _cached ??= LoadFromDisk();
            apply(_cached);
            NormalizeTokenWindow(_cached);
            var json = JsonSerializer.Serialize(_cached, JsonOptions);
            File.WriteAllText(_settingsPath, json);
        }
    }

    private AppPreferences LoadFromDisk()
    {
        if (!File.Exists(_settingsPath))
        {
            var defaults = new AppPreferences();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var value = JsonSerializer.Deserialize<AppPreferences>(json, JsonOptions);
            if (value != null)
            {
                NormalizeLegacyProviderSettings(value, json);
            }
            return value ?? new AppPreferences();
        }
        catch
        {
            return new AppPreferences();
        }
    }

    private static void NormalizeTokenWindow(AppPreferences preferences)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (preferences.TokenWindowDate != today)
        {
            preferences.TokenWindowDate = today;
            preferences.TokensUsedToday = 0;
        }
    }

    private static void NormalizeLegacyProviderSettings(AppPreferences preferences, string rawJson)
    {
        if (string.Equals(preferences.SelectedProvider, "OpenRouter", StringComparison.OrdinalIgnoreCase)
            || string.Equals(preferences.SelectedProvider, "AgentRouter", StringComparison.OrdinalIgnoreCase))
        {
            preferences.SelectedProvider = "Cloudflare";
        }

        if (string.IsNullOrWhiteSpace(preferences.PreferredChatModel))
        {
            preferences.PreferredChatModel = CloudflareProvider.DefaultChatModel;
        }

        if (string.IsNullOrWhiteSpace(preferences.PreferredCodingModel)
            || string.Equals(preferences.PreferredCodingModel, "anthropic/claude-opus-4", StringComparison.OrdinalIgnoreCase)
            || string.Equals(preferences.PreferredCodingModel, "llama-3.3-70b-versatile", StringComparison.OrdinalIgnoreCase))
        {
            preferences.PreferredCodingModel = CloudflareProvider.DefaultCodeModel;
        }

        if (string.IsNullOrWhiteSpace(preferences.PreferredVisionModel)
            || string.Equals(preferences.PreferredVisionModel, "anthropic/claude-opus-4", StringComparison.OrdinalIgnoreCase)
            || string.Equals(preferences.PreferredVisionModel, "meta-llama/llama-4-scout-17b-16e-instruct", StringComparison.OrdinalIgnoreCase))
        {
            preferences.PreferredVisionModel = CloudflareProvider.DefaultVisionModel;
        }
    }

    private static AppPreferences Clone(AppPreferences source)
    {
        return new AppPreferences
        {
            MinimizeToTray = source.MinimizeToTray,
            LaunchOnStartup = source.LaunchOnStartup,
            NotificationsEnabled = source.NotificationsEnabled,
            PrivacyMode = source.PrivacyMode,
            DisableFileUpload = source.DisableFileUpload,
            RequireFileUploadConfirmation = source.RequireFileUploadConfirmation,
            RequireExecutionConfirmation = source.RequireExecutionConfirmation,
            EnableAutomationEngine = source.EnableAutomationEngine,
            EnableBackgroundTasks = source.EnableBackgroundTasks,
            EnableScheduledTasks = source.EnableScheduledTasks,
            EnableVoiceInput = source.EnableVoiceInput,
            ShowRemainingTokens = source.ShowRemainingTokens,
            EnableOfflineIntelligence = source.EnableOfflineIntelligence,
            TokenWarningThreshold = source.TokenWarningThreshold,
            SelectedProvider = source.SelectedProvider,
            CloudflareApiToken = source.CloudflareApiToken,
            CloudflareAccountId = source.CloudflareAccountId,
            SelectedLanguage = source.SelectedLanguage,
            SelectedTheme = source.SelectedTheme,
            AccentColor = source.AccentColor,
            FontSizeScale = source.FontSizeScale,
            IsPremium = source.IsPremium,
            FreeDailyLimit = source.FreeDailyLimit,
            PremiumDailyLimit = source.PremiumDailyLimit,
            TokenWindowDate = source.TokenWindowDate,
            TokensUsedToday = source.TokensUsedToday,
            ChatSessions = source.ChatSessions
                .Select(s => new ChatSessionHistoryRecord
                {
                    Id = s.Id,
                    Title = s.Title,
                    LastUpdated = s.LastUpdated,
                    Preview = s.Preview,
                    Messages = s.Messages
                        .Select(m => new ChatMessageRecord
                        {
                            Content = m.Content,
                            IsUser = m.IsUser,
                            Timestamp = m.Timestamp,
                            MessageKind = m.MessageKind,
                            AttachmentIds = m.AttachmentIds.ToList(),
                            ArtifactIds = m.ArtifactIds.ToList(),
                            ToolTraceSummary = m.ToolTraceSummary,
                            WorkspaceRoot = m.WorkspaceRoot
                        })
                        .ToList()
                })
                .ToList(),
            CommandUsages = source.CommandUsages
                .Select(c => new CommandUsageRecord
                {
                    CommandText = c.CommandText,
                    UseCount = c.UseCount,
                    LastUsed = c.LastUsed
                })
                .ToList(),
            SavedShortcuts = source.SavedShortcuts
                .Select(s => new SavedShortcutRecord
                {
                    Id = s.Id,
                    Name = s.Name,
                    CommandText = s.CommandText,
                    CreatedAt = s.CreatedAt,
                    LastUsed = s.LastUsed
                })
                .ToList(),
            HasCompletedFirstAIAssistantUse = source.HasCompletedFirstAIAssistantUse,
            HasDismissedActionCards = source.HasDismissedActionCards,
            IsAutoModeEnabled = source.IsAutoModeEnabled,
            DefaultWorkspaceRoot = source.DefaultWorkspaceRoot,
            EnableImagePaste = source.EnableImagePaste,
            EnableStreamingResponses = source.EnableStreamingResponses,
            UseLocalOcrFirst = source.UseLocalOcrFirst,
            PreferredChatModel = source.PreferredChatModel,
            PreferredCodingModel = source.PreferredCodingModel,
            PreferredVisionModel = source.PreferredVisionModel,
            EnableArtifactPane = source.EnableArtifactPane
        };
    }
}
