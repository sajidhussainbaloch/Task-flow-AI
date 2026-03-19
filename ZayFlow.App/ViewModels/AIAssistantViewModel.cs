using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ZayFlow.Backend.Contracts;
using ZayFlow.App.Commands;
using ZayFlow.App.Services;
using ZayFlow.App.Services.AI;
using ZayFlow.App.Services.AI.Contracts;

namespace ZayFlow.App.ViewModels;

/// <summary>
/// Local UI chat message model (different from the service's ChatMessage).
/// </summary>
public class UIChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Content { get; set; } = string.Empty;
    public bool IsUser { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string TimeText => Timestamp.ToString("HH:mm");
    public int TokenCost { get; set; }
    public string? Intent { get; set; }
    public bool RequiresConfirmation { get; set; }
    public string ConfirmationMessage { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
    public bool IsSensitiveRequest { get; set; }
}

public class ChatSessionItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "New chat";
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    public string LastUpdatedText => LastUpdated.ToString("MMM dd HH:mm");
    public string Preview { get; set; } = string.Empty;
}

public class ActionTimelineItem
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Intent { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string ResultPreview { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
}

public class AIAssistantViewModel : ViewModelBase
{
    private readonly IAIService _aiService;
    private readonly IntentExecutionService _executionService;
    private readonly ILocalBackendService _backendService;
    private readonly IAppPreferencesService _preferencesService;
    private readonly ILogger<AIAssistantViewModel> _logger;
    private UIChatMessage? _pendingActionMessage;
    private string? _pendingSensitiveUserMessage;
    private ChatMessage? _lastActionableMessage;
    private string _lastExecutionSummary = string.Empty;
    private string _lastUserCommand = string.Empty;
    
    private string _inputText = string.Empty;
    private bool _isTyping;
    private int _estimatedTokens;
    private bool _showingConfirmation;
    private string _confirmationMessage = string.Empty;
    private string _selectedPersonality = "Balanced Assistant";
    private ChatSessionItem? _selectedChat;
    private bool _showWelcome = true;
    private bool _showQuickSuggestions = true;
    private bool _showActionCards = true;
    private bool _isAutoMode;

    public AIAssistantViewModel(IAIService aiService, IntentExecutionService executionService, ILocalBackendService backendService, IAppPreferencesService preferencesService, ILogger<AIAssistantViewModel>? logger)
    {
        _aiService = aiService;
        _executionService = executionService;
        _backendService = backendService;
        _preferencesService = preferencesService;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AIAssistantViewModel>.Instance;
        
        Messages = new ObservableCollection<UIChatMessage>
        {
            new() { Content = "Hello! I'm ZayFlow AI. I can help you organize files, automate tasks, and manage your system. What would you like to do?", IsUser = false, TokenCost = 0 },
        };

        PastChats = new ObservableCollection<ChatSessionItem>();
        LoadSessionHistory();

        Personalities = new ObservableCollection<string>
        {
            "Balanced Assistant",
            "Productivity Coach",
            "Strict Safety Mode",
            "Technical Expert"
        };

        QuickCommands = new ObservableCollection<string>
        {
            "Organize my Downloads by file type",
            "Find duplicate files in Documents",
            "Show folder insights for Downloads",
            "Create a project folder structure for a WPF app",
            "Draft a professional follow-up email"
        };

        ActionTimeline = new ObservableCollection<ActionTimelineItem>();
        LoadSmartSuggestions();

        var pref = _preferencesService.Get();
        var hasPriorUsage = pref.HasCompletedFirstAIAssistantUse
                    || pref.CommandUsages.Count > 0
                    || pref.ChatSessions.Any(s => !string.IsNullOrWhiteSpace(s.Preview));
        ShowQuickSuggestions = !hasPriorUsage;

        SendCommand = new RelayCommand(OnSend, _ => !string.IsNullOrWhiteSpace(InputText) && !IsTyping && !ShowingConfirmation);
        ClearChatCommand = new RelayCommand(_ => OnClearChat());
        ExecuteCommand = new RelayCommand(OnExecute, _ => ShowingConfirmation);
        CancelCommand = new RelayCommand(_ => OnCancel());
        ApplyQuickCommand = new RelayCommand(OnApplyQuickCommand);
        UndoLastActionCommand = new RelayCommand(async _ => await UndoLastActionAsync(), _ => !IsTyping);
        ActionCardCommand = new RelayCommand(OnActionCard);
        DismissActionCardsCommand = new RelayCommand(_ => DismissActionCards());
        ToggleAutoModeCommand = new RelayCommand(_ => IsAutoMode = !IsAutoMode);

        // Load action cards + auto mode from preferences
        ShowActionCards = !pref.HasDismissedActionCards;
        IsAutoMode = pref.IsAutoModeEnabled;
    }

    public ObservableCollection<UIChatMessage> Messages { get; }
    public ObservableCollection<ChatSessionItem> PastChats { get; }
    public ObservableCollection<string> Personalities { get; }
    public ObservableCollection<string> QuickCommands { get; }
    public ObservableCollection<ActionTimelineItem> ActionTimeline { get; }
    public ICommand SendCommand { get; }
    public ICommand ClearChatCommand { get; }
    public ICommand ExecuteCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ApplyQuickCommand { get; }
    public ICommand UndoLastActionCommand { get; }
    public ICommand ActionCardCommand { get; }
    public ICommand DismissActionCardsCommand { get; }
    public ICommand ToggleAutoModeCommand { get; }

    public int RemainingTokens => _aiService.RemainingTokens;
    public int DailyTokenLimit => _aiService.DailyTokenLimit;
    public int UsedTokens => _aiService.TokensUsedToday;
    public string TokenTier => _aiService.TokenTier;
    public bool IsInputEnabled => _aiService.CanUseAI && !ShowingConfirmation;
    public bool ShowUpgradeMessage => !_aiService.CanUseAI;
    public string UpgradeMessage => "No tokens left. Upgrade to Premium to continue AI usage.";

    public string SelectedPersonality
    {
        get => _selectedPersonality;
        set => SetProperty(ref _selectedPersonality, value);
    }

    public ChatSessionItem? SelectedChat
    {
        get => _selectedChat;
        set => SetProperty(ref _selectedChat, value);
    }

    public string InputText
    {
        get => _inputText;
        set
        {
            if (SetProperty(ref _inputText, value))
            {
                EstimatedTokens = value.Length / 4; // rough estimate
                (SendCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsTyping
    {
        get => _isTyping;
        set => SetProperty(ref _isTyping, value);
    }

    public int EstimatedTokens
    {
        get => _estimatedTokens;
        set => SetProperty(ref _estimatedTokens, value);
    }

    public bool ShowingConfirmation
    {
        get => _showingConfirmation;
        set
        {
            if (SetProperty(ref _showingConfirmation, value))
            {
                (ExecuteCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (SendCommand as RelayCommand)?.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(IsInputEnabled));
                OnPropertyChanged(nameof(ShowUpgradeMessage));
            }
        }
    }

    public bool ShowWelcome
    {
        get => _showWelcome;
        set => SetProperty(ref _showWelcome, value);
    }

    public bool ShowQuickSuggestions
    {
        get => _showQuickSuggestions;
        set => SetProperty(ref _showQuickSuggestions, value);
    }

    public bool ShowActionCards
    {
        get => _showActionCards;
        set => SetProperty(ref _showActionCards, value);
    }

    public bool IsAutoMode
    {
        get => _isAutoMode;
        set
        {
            if (SetProperty(ref _isAutoMode, value))
            {
                _preferencesService.Update(p => p.IsAutoModeEnabled = value);
            }
        }
    }

    public string UserGreeting
    {
        get
        {
            var hour = DateTime.Now.Hour;
            var greeting = hour < 12 ? "Good Morning" : hour < 18 ? "Good Afternoon" : "Good Evening";
            return $"{greeting}! What can I do for you?";
        }
    }

    public string ConfirmationMessage
    {
        get => _confirmationMessage;
        set => SetProperty(ref _confirmationMessage, value);
    }

    private async void OnSend(object? _)
    {
        if (string.IsNullOrWhiteSpace(InputText)) return;

        // Hide welcome screen after first message
        if (ShowWelcome && Messages.Count <= 1)
        {
            ShowWelcome = false;
        }

        if (ShowQuickSuggestions)
        {
            ShowQuickSuggestions = false;
            _preferencesService.Update(p => p.HasCompletedFirstAIAssistantUse = true);
        }

        var userMsg = InputText;
        var tokens = EstimatedTokens;

        if (await TryHandleLocalCommandAsync(userMsg))
        {
            InputText = string.Empty;
            return;
        }

        _lastUserCommand = userMsg;
        RecordCommandUsage(userMsg);

        var followUpHandled = await TryHandleFollowUpAsync(userMsg);
        if (followUpHandled)
        {
            InputText = string.Empty;
            return;
        }

        if (LooksLikeUsePreviousOutput(userMsg) && !string.IsNullOrWhiteSpace(_lastExecutionSummary))
        {
            userMsg = $"{userMsg}\n\n[Use previous output context: {_lastExecutionSummary}]";
        }

        Messages.Add(new UIChatMessage
        {
            Content = userMsg,
            IsUser = true,
            TokenCost = tokens
        });

        InputText = string.Empty;
        IsTyping = true;

        try
        {
            // Call AI service to get response
            var cts = new System.Threading.CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            
            var userMessageWithPersonality = $"[{SelectedPersonality}] {userMsg}";
            var aiResponse = await _aiService.ProcessUserMessageAsync(userMessageWithPersonality, false, cts.Token);
            
            IsTyping = false;

            // Add AI response to chat
            if (aiResponse != null)
            {
                var assistantMessage = new UIChatMessage
                {
                    Content = aiResponse.Content,
                    IsUser = false,
                    TokenCost = aiResponse.TokenCost,
                    Intent = aiResponse.ExecutedIntent,
                    RequiresConfirmation = aiResponse.RequiredConfirmation,
                    ConfirmationMessage = aiResponse.ConfirmationMessage,
                    Parameters = aiResponse.IntentParameters,
                    IsSensitiveRequest = aiResponse.IsSensitiveContentRequest
                };

                Messages.Add(assistantMessage);

                if (IsActionableIntent(assistantMessage.Intent))
                {
                    _lastActionableMessage = CloneChatMessage(aiResponse);
                }

                if (assistantMessage.RequiresConfirmation)
                {
                    // Destructive actions need user confirmation
                    _pendingActionMessage = assistantMessage;
                    _pendingSensitiveUserMessage = assistantMessage.IsSensitiveRequest ? userMsg : null;
                    ConfirmationMessage = BuildExecutionPreview(
                        assistantMessage,
                        string.IsNullOrWhiteSpace(assistantMessage.ConfirmationMessage)
                            ? BuildFallbackConfirmation(assistantMessage)
                            : assistantMessage.ConfirmationMessage);
                    ShowingConfirmation = true;
                }
                else if (IsActionableIntent(assistantMessage.Intent))
                {
                    // Non-destructive actionable intents: auto-execute immediately
                    await AutoExecuteIntentAsync(aiResponse);
                }

                UpdateSessionTitle();
                SaveSessionHistory();
            }

            OnPropertyChanged(nameof(RemainingTokens));
            OnPropertyChanged(nameof(UsedTokens));
            OnPropertyChanged(nameof(DailyTokenLimit));
            OnPropertyChanged(nameof(TokenTier));
            OnPropertyChanged(nameof(IsInputEnabled));
            OnPropertyChanged(nameof(ShowUpgradeMessage));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during AI processing");
            IsTyping = false;
            Messages.Add(new UIChatMessage
            {
                Content = $"Error: {ex.Message}",
                IsUser = false,
                TokenCost = 0
            });
        }
    }

    private async void OnExecute(object? _)
    {
        if (_pendingActionMessage == null && string.IsNullOrWhiteSpace(_pendingSensitiveUserMessage)) return;

        try
        {
            ShowingConfirmation = false;
            IsTyping = true;

            ActionResult result;
            if (!string.IsNullOrWhiteSpace(_pendingSensitiveUserMessage) && _pendingActionMessage?.Intent == "privacy_confirmation")
            {
                var response = await _aiService.ProcessUserMessageAsync(_pendingSensitiveUserMessage, true, System.Threading.CancellationToken.None);
                Messages.Add(new UIChatMessage
                {
                    Content = response.Content,
                    IsUser = false,
                    TokenCost = response.TokenCost,
                    Intent = response.ExecutedIntent,
                    RequiresConfirmation = response.RequiredConfirmation,
                    ConfirmationMessage = response.ConfirmationMessage,
                    Parameters = response.IntentParameters
                });

                result = new ActionResult { Success = true, Message = "Sensitive request approved and processed." };
            }
            else
            {
                var serviceMessage = new ChatMessage
                {
                    Content = _pendingActionMessage?.Content ?? string.Empty,
                    Role = "assistant",
                    ExecutedIntent = _pendingActionMessage?.Intent,
                    RequiredConfirmation = _pendingActionMessage?.RequiresConfirmation ?? false,
                    ConfirmationMessage = _pendingActionMessage?.ConfirmationMessage ?? string.Empty,
                    IntentParameters = _pendingActionMessage?.Parameters ?? new Dictionary<string, object>()
                };

                result = await _aiService.ExecuteIntentAsync(serviceMessage, System.Threading.CancellationToken.None);
                AddTimelineItem(serviceMessage.ExecutedIntent ?? "unknown", serviceMessage.IntentParameters, result.Success, result.Message);
            }

            IsTyping = false;

            Messages.Add(new UIChatMessage
            {
                Content = result.Success 
                    ? $"✅ Action completed: {result.Message}"
                    : $"❌ Action failed: {result.Message}",
                IsUser = false,
                TokenCost = 0
            });

            // Feed execution result back to AI memory
            var intentName = _pendingActionMessage?.Intent ?? "unknown";
            _aiService.AddSystemNote($"[Confirmed action: {intentName}] Result: {(result.Success ? "SUCCESS" : "FAILED")} - {result.Message}");
            _lastExecutionSummary = result.Message.Length > 180 ? result.Message[..180] + "…" : result.Message;

            _pendingActionMessage = null;
            _pendingSensitiveUserMessage = null;
            SaveSessionHistory();
            OnPropertyChanged(nameof(RemainingTokens));
            OnPropertyChanged(nameof(UsedTokens));
            OnPropertyChanged(nameof(IsInputEnabled));
            OnPropertyChanged(nameof(ShowUpgradeMessage));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during execution");
            IsTyping = false;
            ShowingConfirmation = false;
            Messages.Add(new UIChatMessage
            {
                Content = $"Execution error: {ex.Message}",
                IsUser = false,
                TokenCost = 0
            });
        }
    }

    private void OnCancel()
    {
        ShowingConfirmation = false;
        ConfirmationMessage = string.Empty;
        _pendingActionMessage = null;
        _pendingSensitiveUserMessage = null;
    }

    private async void OnActionCard(object? parameter)
    {
        var command = parameter?.ToString();
        if (string.IsNullOrWhiteSpace(command)) return;

        // Dismiss action cards after first use
        DismissActionCards();

        // Set input and auto-send
        InputText = command;
        OnSend(null);
    }

    private void DismissActionCards()
    {
        ShowActionCards = false;
        _preferencesService.Update(p => p.HasDismissedActionCards = true);
    }

    /// <summary>
    /// Determines if an intent should be auto-executed (non-destructive action).
    /// </summary>
    private static bool IsActionableIntent(string? intent)
    {
        if (string.IsNullOrWhiteSpace(intent)) return false;
        var actionable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Original intents (1-49)
            "open_application", "open_url", "open_file", "create_document", "generate_text",
            "clean_notes", "plan_tasks", "folder_insights", "find_old_files",
            "summarize_file", "get_disk_info", "read_file", "detect_duplicates",
            "smart_search", "backup_suggestions", "smart_cleanup_schedule",
            "visual_analytics", "change_wallpaper",
            "create_file", "edit_file", "search_web", "run_command", "system_info",
            "set_reminder", "compress_files", "clipboard_action", "translate_text", "download_file",
            "screenshot", "text_to_speech", "wifi_info", "hash_file", "schedule_shutdown", "convert_units",
            "date_time", "generate_password", "quick_math", "ping_host", "process_action",
            // Batch & Productivity (50-60)
            "batch_operations", "quick_note", "focus_mode", "daily_briefing", "generate_report",
            "file_templates", "workspace_snapshot", "productivity_tips", "preview_changes",
            "explain_action", "suggest_workflow",
            // File Tools (61-66)
            "sync_folders", "file_diff", "encrypt_decrypt", "secure_delete",
            "bulk_metadata", "regex_search",
            // Media & Conversion (67-71)
            "data_convert", "text_transform", "image_tools", "pdf_tools", "extract_text",
            // Network (72-75)
            "network_diagnostics", "port_scan", "dns_manage", "hosts_file",
            // System Power (76-81)
            "startup_manager", "service_manager", "env_variables", "performance_report",
            "power_plan", "storage_analyzer",
            // Dev Tools (82-85)
            "git_quick", "api_test", "code_format", "qr_code",
            // Automation (86-88)
            "watch_folder", "scheduled_task", "auto_backup",
            // Workflow (89-90)
            "batch_workflow", "save_template",
            // Hub (91)
            "zayflow_hub"
        };
        return actionable.Contains(intent);
    }

    /// <summary>
    /// Auto-execute an actionable intent without user confirmation and show the result.
    /// Shows live progress updates in chat for long-running operations.
    /// </summary>
    private async Task AutoExecuteIntentAsync(ChatMessage aiMessage)
    {
        try
        {
            IsTyping = true;

            // Create a progress message that updates live in the chat
            var progressMsg = new UIChatMessage
            {
                Content = "⏳ Working on it...",
                IsUser = false,
                TokenCost = 0
            };
            Messages.Add(progressMsg);

            // Wire up live progress reporting
            var progress = new Progress<ActionProgress>(p =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (p.Percent >= 0 && p.Percent <= 100)
                    {
                        var barLength = 20;
                        var filled = (int)(p.Percent / 100 * barLength);
                        var empty = barLength - filled;
                        var bar = new string('█', filled) + new string('░', empty);
                        progressMsg.Content = $"{p.Icon} {p.Status}\n\n[{bar}] {p.Percent:F0}%";
                    }
                    else
                    {
                        progressMsg.Content = $"{p.Icon} {p.Status}";
                    }
                    // Force UI refresh
                    var idx = Messages.IndexOf(progressMsg);
                    if (idx >= 0)
                    {
                        Messages.RemoveAt(idx);
                        Messages.Insert(idx, progressMsg);
                    }
                });
            });

            _executionService.SetProgressReporter(progress);
            var result = await _aiService.ExecuteIntentAsync(aiMessage, System.Threading.CancellationToken.None);
            _executionService.SetProgressReporter(null);
            IsTyping = false;

            // Replace progress message with final result
            var finalIdx = Messages.IndexOf(progressMsg);
            if (finalIdx >= 0) Messages.RemoveAt(finalIdx);

            if (!string.IsNullOrWhiteSpace(result.Message) && result.Message != aiMessage.Content)
            {
                Messages.Add(new UIChatMessage
                {
                    Content = result.Success
                        ? $"✅ {result.Message}"
                        : $"❌ {result.Message}",
                    IsUser = false,
                    TokenCost = 0
                });
            }

            AddTimelineItem(aiMessage.ExecutedIntent ?? "unknown", aiMessage.IntentParameters, result.Success, result.Message);
            _lastExecutionSummary = result.Message.Length > 180 ? result.Message[..180] + "…" : result.Message;

            // Feed execution result back to AI memory so it knows what happened
            _aiService.AddSystemNote($"[Action executed: {aiMessage.ExecutedIntent}] Result: {(result.Success ? "SUCCESS" : "FAILED")} - {result.Message}");

            SaveSessionHistory();
        }
        catch (Exception ex)
        {
            _executionService.SetProgressReporter(null);
            IsTyping = false;
            _logger.LogError(ex, "Auto-execution failed");
            Messages.Add(new UIChatMessage
            {
                Content = $"❌ Action failed: {ex.Message}",
                IsUser = false,
                TokenCost = 0
            });
        }
    }

    private void OnClearChat()
    {
        Messages.Clear();
        _aiService.ClearHistory();
        Messages.Add(new UIChatMessage
        {
            Content = "Chat cleared. How can I help you?",
            IsUser = false,
            TokenCost = 0
        });

        ShowWelcome = true;

        var newSession = new ChatSessionItem
        {
            Title = "Current Session",
            LastUpdated = DateTime.Now,
            Preview = ""
        };
        PastChats.Insert(0, newSession);
        SelectedChat = newSession;

        while (PastChats.Count > 30)
        {
            PastChats.RemoveAt(PastChats.Count - 1);
        }

        SaveSessionHistory();
    }

    private void OnApplyQuickCommand(object? parameter)
    {
        var text = parameter?.ToString();
        if (string.IsNullOrWhiteSpace(text)) return;

        var commandText = ExtractCommandFromChip(text);
        InputText = commandText;
    }

    private string ExtractCommandFromChip(string chipText)
    {
        var marker = " — ";
        var idx = chipText.IndexOf(marker, StringComparison.Ordinal);
        return idx > 0 ? chipText[(idx + marker.Length)..] : chipText;
    }

    private string BuildFallbackConfirmation(UIChatMessage message)
    {
        if (message.IsSensitiveRequest)
        {
            return "This may send sensitive file content to an external AI provider. Continue?";
        }

        var parameterSummary = message.Parameters.Count == 0
            ? "no parameters"
            : string.Join(", ", message.Parameters.Select(p => $"{p.Key}={p.Value}"));

        return $"Execute action '{message.Intent}' with {parameterSummary}?";
    }

    private string BuildExecutionPreview(UIChatMessage message, string baseText)
    {
        var risk = GetRiskLevel(message.Intent, message.Parameters);
        var diff = BuildParameterDiff(message.Intent, message.Parameters);

        // Generate detailed action preview showing exactly what will happen
        var detailedPreview = _executionService.GenerateActionPreview(message.Intent, message.Parameters);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(baseText);
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(detailedPreview))
        {
            // Show the rich file-by-file preview
            sb.AppendLine(detailedPreview);
        }
        else
        {
            // Fallback to generic preview
            sb.AppendLine($"📋 Execution Preview");
            sb.AppendLine($"• Intent: {message.Intent}");
            sb.AppendLine($"• Risk: {risk}");
            sb.AppendLine(diff);
        }

        return sb.ToString().TrimEnd();
    }

    private static string GetRiskLevel(string? intent, Dictionary<string, object> parameters)
    {
        if (string.IsNullOrWhiteSpace(intent)) return "Low";
        if (intent.Equals("delete_files", StringComparison.OrdinalIgnoreCase) ||
            intent.Equals("run_command", StringComparison.OrdinalIgnoreCase) ||
            intent.Equals("schedule_shutdown", StringComparison.OrdinalIgnoreCase) ||
            (intent.Equals("process_action", StringComparison.OrdinalIgnoreCase) &&
             parameters.TryGetValue("action", out var action) && action?.ToString()?.Equals("kill", StringComparison.OrdinalIgnoreCase) == true))
            return "High";

        if (intent.Equals("move_files", StringComparison.OrdinalIgnoreCase) ||
            intent.Equals("rename_files", StringComparison.OrdinalIgnoreCase) ||
            intent.Equals("edit_file", StringComparison.OrdinalIgnoreCase) ||
            intent.Equals("compress_files", StringComparison.OrdinalIgnoreCase) ||
            intent.Equals("download_file", StringComparison.OrdinalIgnoreCase))
            return "Medium";

        return "Low";
    }

    private static string BuildParameterDiff(string? intent, Dictionary<string, object> parameters)
    {
        if (parameters.Count == 0) return "• Changes: none";

        if (string.Equals(intent, "rename_files", StringComparison.OrdinalIgnoreCase))
        {
            var file = parameters.GetValueOrDefault("filePath")?.ToString() ?? "(unknown file)";
            var next = parameters.GetValueOrDefault("newName")?.ToString() ?? "(unknown name)";
            return $"• Change: {file} → {next}";
        }

        if (string.Equals(intent, "move_files", StringComparison.OrdinalIgnoreCase))
        {
            var file = parameters.GetValueOrDefault("filePath")?.ToString() ?? "(unknown file)";
            var destination = parameters.GetValueOrDefault("destination")?.ToString() ?? "(unknown destination)";
            return $"• Change: Move {file} → {destination}";
        }

        if (string.Equals(intent, "edit_file", StringComparison.OrdinalIgnoreCase))
        {
            var file = parameters.GetValueOrDefault("filePath")?.ToString() ?? "(unknown file)";
            var mode = parameters.GetValueOrDefault("mode")?.ToString() ?? "overwrite";
            return $"• Change: Edit {file} (mode: {mode})";
        }

        var preview = string.Join(", ", parameters.Take(4).Select(p => $"{p.Key}={p.Value}"));
        return $"• Params: {preview}";
    }

    private void RecordCommandUsage(string command)
    {
        var normalized = command.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith("/")) return;

        _preferencesService.Update(p =>
        {
            var existing = p.CommandUsages.FirstOrDefault(c => c.CommandText.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                p.CommandUsages.Add(new CommandUsageRecord
                {
                    CommandText = normalized,
                    UseCount = 1,
                    LastUsed = DateTime.Now
                });
            }
            else
            {
                existing.UseCount++;
                existing.LastUsed = DateTime.Now;
            }

            p.CommandUsages = p.CommandUsages
                .OrderByDescending(c => c.UseCount)
                .ThenByDescending(c => c.LastUsed)
                .Take(80)
                .ToList();
        });

        LoadSmartSuggestions();
    }

    private void LoadSmartSuggestions()
    {
        var pref = _preferencesService.Get();
        var suggested = pref.CommandUsages
            .OrderByDescending(c => c.UseCount)
            .ThenByDescending(c => c.LastUsed)
            .Take(6)
            .Select(c => c.CommandText)
            .ToList();

        var shortcuts = pref.SavedShortcuts
            .OrderByDescending(s => s.LastUsed)
            .Take(4)
            .Select(s => $"⚡ {s.Name} — {s.CommandText}")
            .ToList();

        var merged = shortcuts
            .Concat(suggested)
            .Concat(new[]
            {
                "Organize my Downloads by file type",
                "Find duplicate files in Documents",
                "Show folder insights for Downloads",
                "Create a project folder structure for a WPF app",
                "Draft a professional follow-up email"
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        QuickCommands.Clear();
        foreach (var cmd in merged)
            QuickCommands.Add(cmd);
    }

    private async Task<bool> TryHandleFollowUpAsync(string userMsg)
    {
        var text = userMsg.Trim().ToLowerInvariant();

        if (text is "do that again" or "same again" or "retry that" or "repeat last action")
        {
            if (_lastActionableMessage == null)
            {
                Messages.Add(new UIChatMessage { Content = "I don’t have a previous action to repeat yet.", IsUser = false });
                return true;
            }

            Messages.Add(new UIChatMessage { Content = "♻️ Re-running your last action...", IsUser = false });
            await AutoExecuteIntentAsync(_lastActionableMessage);
            return true;
        }

        if (text.Contains("undo last action", StringComparison.OrdinalIgnoreCase) || text == "undo")
        {
            await UndoLastActionAsync();
            return true;
        }

        return false;
    }

    private bool LooksLikeUsePreviousOutput(string userMsg)
        => userMsg.Contains("use previous output", StringComparison.OrdinalIgnoreCase)
           || userMsg.Contains("use last output", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> TryHandleLocalCommandAsync(string userMsg)
    {
        var saveMatch = Regex.Match(userMsg, @"^/save\s+shortcut\s+([^:]+):\s+(.+)$", RegexOptions.IgnoreCase);
        if (saveMatch.Success)
        {
            var name = saveMatch.Groups[1].Value.Trim();
            var command = saveMatch.Groups[2].Value.Trim();
            SaveShortcut(name, command);
            Messages.Add(new UIChatMessage { Content = $"✅ Saved shortcut '{name}'.", IsUser = false });
            return true;
        }

        var runMatch = Regex.Match(userMsg, @"^/run\s+shortcut\s+(.+)$", RegexOptions.IgnoreCase);
        if (runMatch.Success)
        {
            var name = runMatch.Groups[1].Value.Trim();
            var shortcut = _preferencesService.Get().SavedShortcuts
                .FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (shortcut == null)
            {
                Messages.Add(new UIChatMessage { Content = $"Shortcut '{name}' not found.", IsUser = false });
                return true;
            }

            _preferencesService.Update(p =>
            {
                var item = p.SavedShortcuts.First(s => s.Id == shortcut.Id);
                item.LastUsed = DateTime.Now;
            });

            InputText = shortcut.CommandText;
            LoadSmartSuggestions();
            Messages.Add(new UIChatMessage { Content = $"⚡ Loaded shortcut '{name}'. Press Enter to run.", IsUser = false });
            return true;
        }

        var saveLastMatch = Regex.Match(userMsg, @"^save\s+this\s+as\s+shortcut\s+(.+)$", RegexOptions.IgnoreCase);
        if (saveLastMatch.Success)
        {
            var name = saveLastMatch.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(_lastUserCommand))
            {
                Messages.Add(new UIChatMessage { Content = "No previous command found to save.", IsUser = false });
                return true;
            }

            SaveShortcut(name, _lastUserCommand);
            Messages.Add(new UIChatMessage { Content = $"✅ Saved '{_lastUserCommand}' as shortcut '{name}'.", IsUser = false });
            return true;
        }

        return false;
    }

    private void SaveShortcut(string name, string commandText)
    {
        _preferencesService.Update(p =>
        {
            var existing = p.SavedShortcuts.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                p.SavedShortcuts.Add(new SavedShortcutRecord
                {
                    Name = name,
                    CommandText = commandText,
                    CreatedAt = DateTime.Now,
                    LastUsed = DateTime.Now
                });
            }
            else
            {
                existing.CommandText = commandText;
                existing.LastUsed = DateTime.Now;
            }

            p.SavedShortcuts = p.SavedShortcuts
                .OrderByDescending(s => s.LastUsed)
                .Take(40)
                .ToList();
        });

        LoadSmartSuggestions();
    }

    private async Task UndoLastActionAsync()
    {
        try
        {
            IsTyping = true;
            var result = await _backendService.UndoLastAsync();
            IsTyping = false;

            var success = result.FailureCount == 0;
            var msg = success
                ? $"↩️ Undo completed. Reverted {result.SuccessCount} action(s)."
                : $"↩️ Undo completed with warnings. Reverted {result.SuccessCount}, failed {result.FailureCount}.";

            Messages.Add(new UIChatMessage { Content = msg, IsUser = false });
            AddTimelineItem("undo_last", new Dictionary<string, object>(), success, result.Message);
            _lastExecutionSummary = msg;
        }
        catch (Exception ex)
        {
            IsTyping = false;
            Messages.Add(new UIChatMessage { Content = $"❌ Undo failed: {ex.Message}", IsUser = false });
        }
    }

    private void AddTimelineItem(string intent, Dictionary<string, object> parameters, bool success, string message)
    {
        ActionTimeline.Insert(0, new ActionTimelineItem
        {
            Timestamp = DateTime.Now,
            Intent = intent,
            Success = success,
            ResultPreview = message.Length > 140 ? message[..140] + "…" : message,
            Parameters = parameters.ToDictionary(k => k.Key, v => v.Value)
        });

        while (ActionTimeline.Count > 40)
            ActionTimeline.RemoveAt(ActionTimeline.Count - 1);
    }

    private static ChatMessage CloneChatMessage(ChatMessage source)
    {
        return new ChatMessage
        {
            Content = source.Content,
            Role = source.Role,
            ExecutedIntent = source.ExecutedIntent,
            TokenCost = source.TokenCost,
            RequiredConfirmation = source.RequiredConfirmation,
            ConfirmationMessage = source.ConfirmationMessage,
            IsSensitiveContentRequest = source.IsSensitiveContentRequest,
            IntentParameters = source.IntentParameters.ToDictionary(k => k.Key, v => v.Value)
        };
    }

    private void UpdateSessionTitle()
    {
        if (SelectedChat == null)
        {
            SelectedChat = new ChatSessionItem { Title = "Chat " + (PastChats.Count + 1) };
            PastChats.Insert(0, SelectedChat);
        }

        var firstUser = Messages.FirstOrDefault(m => m.IsUser)?.Content;
        if (!string.IsNullOrWhiteSpace(firstUser))
        {
            SelectedChat.Title = firstUser.Length > 40 ? firstUser[..40] + "…" : firstUser;
        }

        var latest = Messages.LastOrDefault(m => !string.IsNullOrWhiteSpace(m.Content))?.Content ?? string.Empty;
        SelectedChat.Preview = latest.Length > 100 ? latest[..100] + "…" : latest;

        SelectedChat.LastUpdated = DateTime.Now;
        OnPropertyChanged(nameof(SelectedChat));
    }

    private void LoadSessionHistory()
    {
        var preferences = _preferencesService.Get();
        var sessions = preferences.ChatSessions
            .OrderByDescending(s => s.LastUpdated)
            .Select(s => new ChatSessionItem
            {
                Id = s.Id,
                Title = string.IsNullOrWhiteSpace(s.Title) ? "Chat" : s.Title,
                LastUpdated = s.LastUpdated,
                Preview = s.Preview
            })
            .ToList();

        PastChats.Clear();
        foreach (var session in sessions)
        {
            PastChats.Add(session);
        }

        if (PastChats.Count == 0)
        {
            PastChats.Add(new ChatSessionItem { Title = "Current Session", LastUpdated = DateTime.Now });
        }

        SelectedChat = PastChats[0];
    }

    private void SaveSessionHistory()
    {
        // Build messages for the current session
        var currentMessages = Messages
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => new ChatMessageRecord
            {
                Content = m.Content,
                IsUser = m.IsUser,
                Timestamp = DateTime.Now
            })
            .ToList();

        // Update current session's messages in preferences
        var currentId = SelectedChat?.Id;

        var snapshot = PastChats
            .OrderByDescending(s => s.LastUpdated)
            .Take(30)
            .Select(s => new ChatSessionHistoryRecord
            {
                Id = s.Id,
                Title = s.Title,
                LastUpdated = s.LastUpdated,
                Preview = s.Preview,
                Messages = s.Id == currentId ? currentMessages : GetSavedMessagesForSession(s.Id)
            })
            .ToList();

        _preferencesService.Update(p => p.ChatSessions = snapshot);
    }

    private List<ChatMessageRecord> GetSavedMessagesForSession(string sessionId)
    {
        var pref = _preferencesService.Get();
        var session = pref.ChatSessions.FirstOrDefault(s => s.Id == sessionId);
        return session?.Messages ?? new List<ChatMessageRecord>();
    }

    /// <summary>
    /// Load a previous session's messages into the chat view by session ID.
    /// </summary>
    public void LoadSessionById(string sessionId)
    {
        var pref = _preferencesService.Get();
        var session = pref.ChatSessions.FirstOrDefault(s => s.Id == sessionId);
        if (session == null || session.Messages.Count == 0) return;

        // Save current session first
        SaveSessionHistory();

        // Clear current chat
        Messages.Clear();
        _aiService.ClearHistory();

        // Restore messages
        foreach (var msg in session.Messages)
        {
            Messages.Add(new UIChatMessage
            {
                Content = msg.Content,
                IsUser = msg.IsUser,
                TokenCost = 0
            });
        }

        // Select this session
        var chatItem = PastChats.FirstOrDefault(c => c.Id == sessionId);
        if (chatItem != null)
            SelectedChat = chatItem;

        ShowWelcome = false;
        OnPropertyChanged(nameof(Messages));
    }
}
