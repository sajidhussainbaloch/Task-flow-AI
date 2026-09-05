using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Commands;
using ZayFlow.App.Services;
using ZayFlow.App.Services.AI;
using ZayFlow.App.Services.AI.Contracts;
using ZayFlow.App.Services.Assistant;
using ZayFlow.App.Services.CodeGeneration.Agent;
using ZayFlow.App.Services.CodeGeneration.Checkpointing;
using ZayFlow.App.Services.CodeGeneration.Contracts;
using ZayFlow.App.Services.CodeGeneration.Notes;
using ZayFlow.Backend.Contracts;

namespace ZayFlow.App.ViewModels;

public class UIAttachmentItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public string PreviewText { get; set; } = string.Empty;
    public string OcrText { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }

    public AssistantAttachment ToAssistantAttachment() => new()
    {
        Id = Id,
        Title = Title,
        LocalPath = LocalPath,
        PreviewText = PreviewText,
        OcrText = OcrText,
        Width = Width,
        Height = Height,
        Kind = "image"
    };

    public static UIAttachmentItem FromAssistantAttachment(AssistantAttachment attachment) => new()
    {
        Id = attachment.Id,
        Title = attachment.Title,
        LocalPath = attachment.LocalPath,
        PreviewText = attachment.PreviewText,
        OcrText = attachment.OcrText,
        Width = attachment.Width,
        Height = attachment.Height
    };
}

public class UIArtifactItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string SecondaryContent { get; set; } = string.Empty;
    public string Language { get; set; } = "text";
    public string FilePath { get; set; } = string.Empty;
    public string Kind { get; set; } = AssistantArtifactKind.CodePreview.ToString();
    public bool IsPreviewOnly { get; set; } = true;
    public bool IsImageArtifact => Kind.Equals(AssistantArtifactKind.ImageAttachment.ToString(), StringComparison.OrdinalIgnoreCase);

    public static UIArtifactItem FromArtifact(AssistantArtifact artifact) => new()
    {
        Id = artifact.Id,
        Title = artifact.Title,
        Summary = artifact.Summary,
        Content = artifact.Content,
        SecondaryContent = artifact.SecondaryContent,
        Language = artifact.Language,
        FilePath = artifact.FilePath,
        Kind = artifact.Kind.ToString(),
        IsPreviewOnly = artifact.IsPreviewOnly
    };
}

public class UIToolTraceItem
{
    public string Name { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    public static UIToolTraceItem FromTool(ToolInvocation tool) => new()
    {
        Name = tool.Name,
        Summary = tool.Summary,
        Status = tool.Status.ToString()
    };
}

public class UIChatMessage : ViewModelBase
{
    private string _content = string.Empty;
    private string _toolTraceSummary = string.Empty;
    private CodeSessionInfo? _codeSession;

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Content
    {
        get => _content;
        set => SetProperty(ref _content, value);
    }
    public bool IsUser { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string TimeText => Timestamp.ToString("HH:mm");
    public int TokenCost { get; set; }
    public string? Intent { get; set; }
    public bool RequiresConfirmation { get; set; }
    public string ConfirmationMessage { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsSensitiveRequest { get; set; }
    public string MessageKind { get; set; } = "chat";
    public string ToolTraceSummary
    {
        get => _toolTraceSummary;
        set => SetProperty(ref _toolTraceSummary, value);
    }
    public AssistantTurnMode TurnMode { get; set; } = AssistantTurnMode.Chat;
    public string WorkspaceRoot { get; set; } = string.Empty;
    public CodeSessionInfo? CodeSession
    {
        get => _codeSession;
        set
        {
            if (SetProperty(ref _codeSession, value))
            {
                OnPropertyChanged(nameof(HasCodeSession));
            }
        }
    }
    public bool HasCodeSession => CodeSession != null;
    public ObservableCollection<UIAttachmentItem> Attachments { get; } = new();
    public ObservableCollection<UIArtifactItem> Artifacts { get; } = new();
    public ObservableCollection<UIToolTraceItem> ToolInvocations { get; } = new();
}

public class ChatSessionItem : ViewModelBase
{
    private string _title = "New chat";
    private bool _isMenuOpen;

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    public string LastUpdatedText => LastUpdated.ToString("MMM dd HH:mm");
    public string Preview { get; set; } = string.Empty;

    /// <summary>Whether the 3-dot context menu is open for this item.</summary>
    public bool IsMenuOpen
    {
        get => _isMenuOpen;
        set => SetProperty(ref _isMenuOpen, value);
    }
}

public class ActionTimelineItem
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Intent { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string ResultPreview { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class PlanStepViewModel : ViewModelBase
{
    private string _statusIcon = "⬜";
    private string _riskBadge = "🟢";

    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = "safe";

    public string StatusIcon
    {
        get => _statusIcon;
        set => SetProperty(ref _statusIcon, value);
    }

    public string RiskBadge
    {
        get => _riskBadge;
        set => SetProperty(ref _riskBadge, value);
    }

    public static PlanStepViewModel FromStep(PlanStepItem step) => new()
    {
        Id = step.Id,
        Description = step.Description,
        Action = step.Action,
        Target = step.Target,
        RiskLevel = step.RiskLevel,
        RiskBadge = step.RiskLevel switch
        {
            "risky" => "🔴",
            "caution" => "🟡",
            _ => "🟢"
        }
    };

    public void UpdateStatus(PlanStepStatus status)
    {
        StatusIcon = status switch
        {
            PlanStepStatus.Pending => "⬜",
            PlanStepStatus.InProgress => "🔄",
            PlanStepStatus.Completed => "✅",
            PlanStepStatus.Failed => "❌",
            PlanStepStatus.Skipped => "⏭️",
            _ => "⬜"
        };
    }
}

public class AIAssistantViewModel : ViewModelBase
{
    private readonly IAIService _aiService;
    private readonly IntentExecutionService _executionService;
    private readonly ILocalBackendService _backendService;
    private readonly IAppPreferencesService _preferencesService;
    private readonly IClipboardAttachmentService _clipboardAttachmentService;
    private readonly IAssistantOrchestrator _orchestrator;
    private readonly RetryOrchestrationService _retryService;
    private readonly WorkflowNotesService _workflowNotes;
    private readonly WorkflowCheckpointService _checkpointService;
    private readonly ILogger<AIAssistantViewModel> _logger;

    private UIChatMessage? _pendingActionMessage;
    private string? _pendingSensitiveUserMessage;
    private List<AssistantAttachment> _pendingSensitiveAttachments = new();
    private ChatMessage? _lastActionableMessage;
    private string _lastExecutionSummary = string.Empty;
    private string _lastUserCommand = string.Empty;
    private string _lastCreatedFilePath = string.Empty;
    /// <summary>Phase 2 — Last completed code session, reused as PreviousCodeSession on the next code turn.</summary>
    private ZayFlow.App.Services.CodeGeneration.Contracts.CodeGenerationSession? _lastCodeSession;
    private CancellationTokenSource? _activeExecutionCts;
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
    private string _workspaceRoot = string.Empty;
    private bool _showArtifactPane = true;
    private UIArtifactItem? _selectedArtifact;
    /// <summary>Phase 3 — When true, code requests go through Plan Mode (propose → approve → execute).</summary>
    private bool _isPlanMode;
    private bool _showingPlan;
    private bool _isExecutingPlan;
    private string _planTitle = string.Empty;
    private string _planSummary = string.Empty;
    private string _planOverallRisk = string.Empty;
    private string _planProgress = string.Empty;
    private ExecutionPlan? _currentPlan;
    private bool _showingResume;
    private string _resumeSummary = string.Empty;
    private WorkflowCheckpoint? _resumableCheckpoint;

    public AIAssistantViewModel(
        IAIService aiService,
        IntentExecutionService executionService,
        ILocalBackendService backendService,
        IAppPreferencesService preferencesService,
        IAssistantOrchestrator orchestrator,
        RetryOrchestrationService retryService,
        WorkflowNotesService workflowNotes,
        WorkflowCheckpointService checkpointService,
        IClipboardAttachmentService? clipboardAttachmentService = null,
        ILogger<AIAssistantViewModel>? logger = null)
    {
        _aiService = aiService;
        _executionService = executionService;
        _backendService = backendService;
        _preferencesService = preferencesService;
        _orchestrator = orchestrator;
        _retryService = retryService;
        _workflowNotes = workflowNotes;
        _checkpointService = checkpointService;
        _clipboardAttachmentService = clipboardAttachmentService ?? new ClipboardAttachmentService();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AIAssistantViewModel>.Instance;

        Messages = new ObservableCollection<UIChatMessage>();
        PastChats = new ObservableCollection<ChatSessionItem>();
        Personalities = new ObservableCollection<string> { "Balanced Assistant", "Coding Agent", "Vision Analyst", "Desktop Automator" };
        QuickCommands = new ObservableCollection<string>();
        ActionTimeline = new ObservableCollection<ActionTimelineItem>();
        PendingAttachments = new ObservableCollection<UIAttachmentItem>();
        ArtifactPaneItems = new ObservableCollection<UIArtifactItem>();

        var pref = _preferencesService.Get();
        WorkspaceRoot = pref.DefaultWorkspaceRoot;
        ShowArtifactPane = pref.EnableArtifactPane;
        ShowActionCards = !pref.HasDismissedActionCards;
        IsAutoMode = pref.IsAutoModeEnabled;

        SendCommand = new RelayCommand(OnSend, _ => (PendingAttachments.Count > 0 || !string.IsNullOrWhiteSpace(InputText)) && !IsTyping);
        ClearChatCommand = new RelayCommand(_ => OnClearChat());
        ExecuteCommand = new RelayCommand(OnExecute, _ => ShowingConfirmation);
        CancelCommand = new RelayCommand(_ => OnCancel(), _ => ShowingConfirmation || (IsTyping && _activeExecutionCts != null));
        ApplyQuickCommand = new RelayCommand(OnApplyQuickCommand);
        UndoLastActionCommand = new RelayCommand(async _ => await UndoLastActionAsync(), _ => !IsTyping && _lastActionableMessage != null);
        ActionCardCommand = new RelayCommand(OnActionCard);
        DismissActionCardsCommand = new RelayCommand(_ => DismissActionCards());
        ToggleAutoModeCommand = new RelayCommand(_ => IsAutoMode = !IsAutoMode);
        TogglePlanModeCommand = new RelayCommand(_ => IsPlanMode = !IsPlanMode);
        ApprovePlanCommand = new RelayCommand(async _ => await OnApprovePlanAsync(), _ => ShowingPlan && !IsExecutingPlan);
        RejectPlanCommand = new RelayCommand(_ => OnRejectPlan(), _ => ShowingPlan && !IsExecutingPlan);
        SkipStepCommand = new RelayCommand(async _ => await OnSkipStepAsync(), _ => ShowingPlan);
        ResumeWorkflowCommand = new RelayCommand(async _ => await OnResumeWorkflowAsync(), _ => ShowingResume);
        DiscardResumeCommand = new RelayCommand(_ => OnDiscardResume(), _ => ShowingResume);
        PasteImageCommand = new RelayCommand(_ => OnPasteImage(), _ => _preferencesService.Get().EnableImagePaste && !IsTyping);
        AttachImageFromFileCommand = new RelayCommand(_ => OnAttachImageFromFile(), _ => !IsTyping);
        AttachFileCommand = new RelayCommand(_ => OnAttachFile(), _ => !IsTyping);
        RemovePendingAttachmentCommand = new RelayCommand(OnRemovePendingAttachment);
        SelectArtifactCommand = new RelayCommand(OnSelectArtifact);
        ToggleArtifactPaneCommand = new RelayCommand(_ => ShowArtifactPane = !ShowArtifactPane);
        CopyArtifactCodeCommand = new RelayCommand(OnCopyArtifactCode);
        NewChatCommand = new RelayCommand(_ => OnClearChat());
        RenameChatCommand = new RelayCommand(OnRenameChat);
        DeleteChatCommand = new RelayCommand(OnDeleteChat);
        LoadChatCommand = new RelayCommand(OnLoadChat);
        ToggleChatMenuCommand = new RelayCommand(OnToggleChatMenu);

        LoadSessionHistory();
        LoadSmartSuggestions();

        if (Messages.Count == 0)
        {
            ResetWelcomeMessage();
        }

        var hasPriorUsage = pref.HasCompletedFirstAIAssistantUse
            || pref.CommandUsages.Count > 0
            || pref.ChatSessions.Any(session => !string.IsNullOrWhiteSpace(session.Preview));
        ShowQuickSuggestions = !hasPriorUsage;

        // Fire-and-forget: check for resumable workflows
        _ = CheckForResumableWorkflowAsync();
    }

    public ObservableCollection<UIChatMessage> Messages { get; }
    public ObservableCollection<ChatSessionItem> PastChats { get; }
    public ObservableCollection<string> Personalities { get; }
    public ObservableCollection<string> QuickCommands { get; }
    public ObservableCollection<ActionTimelineItem> ActionTimeline { get; }
    public ObservableCollection<UIAttachmentItem> PendingAttachments { get; }
    public ObservableCollection<UIArtifactItem> ArtifactPaneItems { get; }

    public ICommand SendCommand { get; }
    public ICommand ClearChatCommand { get; }
    public ICommand ExecuteCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ApplyQuickCommand { get; }
    public ICommand UndoLastActionCommand { get; }
    public ICommand ActionCardCommand { get; }
    public ICommand DismissActionCardsCommand { get; }
    public ICommand ToggleAutoModeCommand { get; }
    public ICommand TogglePlanModeCommand { get; }
    public ICommand ApprovePlanCommand { get; }
    public ICommand RejectPlanCommand { get; }
    public ICommand SkipStepCommand { get; }
    public ICommand ResumeWorkflowCommand { get; }
    public ICommand DiscardResumeCommand { get; }
    public ICommand PasteImageCommand { get; }
    public ICommand AttachImageFromFileCommand { get; }
    public ICommand AttachFileCommand { get; }
    public ICommand RemovePendingAttachmentCommand { get; }
    public ICommand SelectArtifactCommand { get; }
    public ICommand ToggleArtifactPaneCommand { get; }
    public ICommand CopyArtifactCodeCommand { get; }
    public ICommand NewChatCommand { get; }
    public ICommand RenameChatCommand { get; }
    public ICommand DeleteChatCommand { get; }
    public ICommand LoadChatCommand { get; }
    public ICommand ToggleChatMenuCommand { get; }

    public int RemainingTokens => _aiService.RemainingTokens;
    public int DailyTokenLimit => _aiService.DailyTokenLimit;
    public int UsedTokens => _aiService.TokensUsedToday;
    public string TokenTier => _aiService.TokenTier;
    public bool IsInputEnabled => _aiService.CanUseAI && !IsTyping;
    public bool ShowUpgradeMessage => !_aiService.CanUseAI;
    public string UpgradeMessage => "No tokens left. Upgrade to Premium to continue AI usage.";
    public bool HasArtifacts => ArtifactPaneItems.Count > 0;

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
                EstimatedTokens = Math.Max(1, value.Length / 4);
                RaiseCommandStates();
            }
        }
    }

    public bool IsTyping
    {
        get => _isTyping;
        set
        {
            if (SetProperty(ref _isTyping, value))
            {
                RaiseCommandStates();
                OnPropertyChanged(nameof(IsInputEnabled));
            }
        }
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
                RaiseCommandStates();
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

    /// <summary>Phase 3 — When enabled, code requests go through Plan Mode.</summary>
    public bool IsPlanMode
    {
        get => _isPlanMode;
        set => SetProperty(ref _isPlanMode, value);
    }

    public bool ShowingPlan
    {
        get => _showingPlan;
        set
        {
            if (SetProperty(ref _showingPlan, value))
                RaiseCommandStates();
        }
    }

    public bool IsExecutingPlan
    {
        get => _isExecutingPlan;
        set
        {
            if (SetProperty(ref _isExecutingPlan, value))
                RaiseCommandStates();
        }
    }

    public string PlanTitle
    {
        get => _planTitle;
        set => SetProperty(ref _planTitle, value);
    }

    public string PlanSummary
    {
        get => _planSummary;
        set => SetProperty(ref _planSummary, value);
    }

    public string PlanOverallRisk
    {
        get => _planOverallRisk;
        set => SetProperty(ref _planOverallRisk, value);
    }

    public string PlanProgress
    {
        get => _planProgress;
        set => SetProperty(ref _planProgress, value);
    }

    public ObservableCollection<PlanStepViewModel> PlanSteps { get; } = new();

    public bool ShowingResume
    {
        get => _showingResume;
        set
        {
            if (SetProperty(ref _showingResume, value))
                RaiseCommandStates();
        }
    }

    public string ResumeSummary
    {
        get => _resumeSummary;
        set => SetProperty(ref _resumeSummary, value);
    }

    public string WorkspaceRoot
    {
        get => _workspaceRoot;
        set
        {
            if (SetProperty(ref _workspaceRoot, value ?? string.Empty))
            {
                _preferencesService.Update(p => p.DefaultWorkspaceRoot = _workspaceRoot);
            }
        }
    }

    public bool ShowArtifactPane
    {
        get => _showArtifactPane;
        set
        {
            if (SetProperty(ref _showArtifactPane, value))
            {
                _preferencesService.Update(p => p.EnableArtifactPane = value);
            }
        }
    }

    public UIArtifactItem? SelectedArtifact
    {
        get => _selectedArtifact;
        set => SetProperty(ref _selectedArtifact, value);
    }

    public string UserGreeting
    {
        get
        {
            var hour = DateTime.Now.Hour;
            var greeting = hour < 12 ? "Good Morning" : hour < 18 ? "Good Afternoon" : "Good Evening";
            return $"{greeting}. What do you want to build or automate?";
        }
    }

    public string ConfirmationMessage
    {
        get => _confirmationMessage;
        set => SetProperty(ref _confirmationMessage, value);
    }

    public void AddDroppedImage(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        PendingAttachments.Add(new UIAttachmentItem
        {
            Title = Path.GetFileName(filePath),
            LocalPath = filePath,
            PreviewText = "Dropped image"
        });
        RaiseCommandStates();
    }

    public void LoadSessionById(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var preferences = _preferencesService.Get();
        var session = preferences.ChatSessions.FirstOrDefault(item => item.Id == sessionId);
        if (session == null)
        {
            return;
        }

        Messages.Clear();
        ArtifactPaneItems.Clear();
        PendingAttachments.Clear();
        SelectedArtifact = null;

        foreach (var record in session.Messages.OrderBy(item => item.Timestamp))
        {
            Messages.Add(new UIChatMessage
            {
                Content = record.Content,
                IsUser = record.IsUser,
                Timestamp = record.Timestamp,
                MessageKind = string.IsNullOrWhiteSpace(record.MessageKind) ? (record.IsUser ? "chat" : "assistant") : record.MessageKind,
                ToolTraceSummary = record.ToolTraceSummary,
                WorkspaceRoot = record.WorkspaceRoot
            });
        }

        if (Messages.Count == 0)
        {
            ResetWelcomeMessage();
            ShowWelcome = true;
        }
        else
        {
            ShowWelcome = false;
        }

        SelectedChat = PastChats.FirstOrDefault(item => item.Id == sessionId)
            ?? new ChatSessionItem { Id = session.Id, Title = session.Title, LastUpdated = session.LastUpdated, Preview = session.Preview };
    }

    private async void OnSend(object? _)
    {
        if (string.IsNullOrWhiteSpace(InputText) && PendingAttachments.Count == 0)
        {
            return;
        }

        var rawInput = InputText.Trim();
        if (await TryHandleFollowUpAsync(rawInput))
        {
            InputText = string.Empty;
            return;
        }

        if (await TryHandleLocalCommandAsync(rawInput))
        {
            InputText = string.Empty;
            return;
        }

        ShowWelcome = false;
        ShowQuickSuggestions = false;
        _preferencesService.Update(p => p.HasCompletedFirstAIAssistantUse = true);

        var attachments = PendingAttachments.Select(item => item.ToAssistantAttachment()).ToList();
        var userMessage = new UIChatMessage
        {
            Content = string.IsNullOrWhiteSpace(rawInput) ? "[Attached image]" : rawInput,
            IsUser = true,
            Timestamp = DateTime.Now,
            TokenCost = EstimatedTokens,
            MessageKind = attachments.Count > 0 ? "vision" : "chat",
            WorkspaceRoot = WorkspaceRoot
        };
        foreach (var attachment in PendingAttachments)
        {
            userMessage.Attachments.Add(new UIAttachmentItem
            {
                Id = attachment.Id,
                Title = attachment.Title,
                LocalPath = attachment.LocalPath,
                PreviewText = attachment.PreviewText,
                OcrText = attachment.OcrText,
                Width = attachment.Width,
                Height = attachment.Height
            });
        }
        Messages.Add(userMessage);

        _lastUserCommand = rawInput;
        RecordCommandUsage(string.IsNullOrWhiteSpace(rawInput) ? "[image]" : rawInput);
        InputText = string.Empty;
        PendingAttachments.Clear();
        IsTyping = true;

        // ── Phase 3: Plan Mode interception ──
        if (IsPlanMode && _orchestrator.GetPendingPlan() == null && !ShowingPlan)
        {
            try
            {
                using var planCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
                var workspaceFiles = GetWorkspaceFiles();
                var planResult = await _orchestrator.GeneratePlanForRequestAsync(
                    rawInput, WorkspaceRoot, workspaceFiles, planCts.Token);

                var planMessage = new UIChatMessage
                {
                    Content = planResult.Message,
                    IsUser = false,
                    Intent = planResult.Intent,
                    MessageKind = "plan",
                    ToolTraceSummary = planResult.ToolTraceSummary,
                    WorkspaceRoot = WorkspaceRoot
                };
                Messages.Add(planMessage);

                var plan = _orchestrator.GetPendingPlan();
                if (plan != null)
                {
                    PopulatePlanCard(plan);
                }

                SaveSessionHistory();
                RefreshUsageProperties();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Plan generation failed");
                Messages.Add(new UIChatMessage
                {
                    Content = $"Plan generation failed: {ex.Message}",
                    IsUser = false,
                    MessageKind = "error"
                });
            }
            finally
            {
                IsTyping = false;
            }
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            UIChatMessage? progressMessage = null;
            var request = new AssistantTurnRequest
            {
                UserMessage = string.IsNullOrWhiteSpace(rawInput) ? "Analyze the attached image and help me with it." : rawInput,
                WorkspaceRoot = WorkspaceRoot,
                LastCreatedFilePath = _lastCreatedFilePath,
                LastExecutionSummary = _lastExecutionSummary,
                PrivacyMode = _preferencesService.Get().PrivacyMode,
                UseLocalOcrFirst = _preferencesService.Get().UseLocalOcrFirst,
                Attachments = attachments,
                RecentMessages = _aiService.ConversationHistory.ToList(),
                PreviousCodeSession = _lastCodeSession  // Phase 2: multi-turn continuation
            };

            if (_preferencesService.Get().EnableStreamingResponses)
            {
                progressMessage = new UIChatMessage
                {
                    Content = "Preparing request...",
                    IsUser = false,
                    MessageKind = "progress",
                    WorkspaceRoot = WorkspaceRoot,
                    Timestamp = DateTime.Now
                };
                Messages.Add(progressMessage);

                request.CodeProgress = new Progress<CodeGenerationStageEvent>(evt =>
                {
                    progressMessage.Content = BuildCodeEngineProgressContent(evt);
                    progressMessage.ToolTraceSummary = evt.Message;
                    progressMessage.CodeSession = new CodeSessionInfo
                    {
                        CurrentStage = evt.Stage.ToString(),
                        IterationsUsed = evt.Iteration,
                        GeneratedFileCount = evt.GeneratedFileCount,
                        ConfidenceScore = evt.ConfidenceScore,
                        IsBlocked = evt.Status == CodeGenerationEventStatus.Blocked || !string.IsNullOrWhiteSpace(evt.BlockedReason),
                        BlockedReason = evt.BlockedReason,
                        OutputType = "code",
                        VerificationSummary = evt.Message
                    };
                });
            }

            var aiResponse = await _aiService.ProcessAssistantTurnAsync(request, cts.Token);
            if (progressMessage != null)
            {
                Messages.Remove(progressMessage);
            }

            // Phase 2: persist code session for multi-turn continuation
            if (aiResponse.RawCodeSession?.FinalOutput != null)
                _lastCodeSession = aiResponse.RawCodeSession;
            else if (aiResponse.TurnMode != AssistantTurnMode.Code)
                _lastCodeSession = null; // non-code response breaks continuity

            var assistantMessage = CreateUiMessage(aiResponse);
            Messages.Add(assistantMessage);
            UpdateArtifactPane(assistantMessage);
            UpdateLastCreatedFilePath(assistantMessage);

            if (IsActionableIntent(assistantMessage.Intent))
            {
                _lastActionableMessage = CloneChatMessage(aiResponse);
            }

            if (assistantMessage.RequiresConfirmation)
            {
                _pendingActionMessage = assistantMessage;
                _pendingSensitiveUserMessage = assistantMessage.IsSensitiveRequest ? rawInput : null;
                _pendingSensitiveAttachments = assistantMessage.Attachments.Select(item => item.ToAssistantAttachment()).ToList();
                ConfirmationMessage = BuildExecutionPreview(
                    assistantMessage,
                    string.IsNullOrWhiteSpace(assistantMessage.ConfirmationMessage)
                        ? BuildFallbackConfirmation(assistantMessage)
                        : assistantMessage.ConfirmationMessage);
                ShowingConfirmation = true;
            }
            else if (IsAutoMode && IsActionableIntent(assistantMessage.Intent))
            {
                await AutoExecuteIntentAsync(aiResponse);
            }

            UpdateSessionTitle();
            SaveSessionHistory();
            RefreshUsageProperties();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during AI processing");
            Messages.Add(new UIChatMessage
            {
                Content = $"Error: {ex.Message}",
                IsUser = false,
                MessageKind = "error"
            });
        }
        finally
        {
            IsTyping = false;
        }
    }

    private async void OnExecute(object? _)
    {
        if (_pendingActionMessage == null && string.IsNullOrWhiteSpace(_pendingSensitiveUserMessage))
        {
            return;
        }

        try
        {
            ShowingConfirmation = false;
            IsTyping = true;
            ActionResult result;

            if (!string.IsNullOrWhiteSpace(_pendingSensitiveUserMessage) && string.Equals(_pendingActionMessage?.Intent, "privacy_confirmation", StringComparison.OrdinalIgnoreCase))
            {
                var response = await _aiService.ProcessAssistantTurnAsync(new AssistantTurnRequest
                {
                    UserMessage = _pendingSensitiveUserMessage,
                    BypassPrivacyConfirmation = true,
                    PrivacyMode = false,
                    WorkspaceRoot = WorkspaceRoot,
                    LastCreatedFilePath = _lastCreatedFilePath,
                    LastExecutionSummary = _lastExecutionSummary,
                    UseLocalOcrFirst = _preferencesService.Get().UseLocalOcrFirst,
                    Attachments = _pendingSensitiveAttachments,
                    RecentMessages = _aiService.ConversationHistory.ToList()
                }, CancellationToken.None);

                var nextMessage = CreateUiMessage(response);
                Messages.Add(nextMessage);
                UpdateArtifactPane(nextMessage);
                UpdateLastCreatedFilePath(nextMessage);

                if (nextMessage.RequiresConfirmation)
                {
                    _pendingActionMessage = nextMessage;
                    _pendingSensitiveUserMessage = null;
                    _pendingSensitiveAttachments = nextMessage.Attachments.Select(item => item.ToAssistantAttachment()).ToList();
                    ConfirmationMessage = BuildExecutionPreview(nextMessage, nextMessage.ConfirmationMessage);
                    ShowingConfirmation = true;
                    return;
                }

                if (IsActionableIntent(nextMessage.Intent))
                {
                    await AutoExecuteIntentAsync(response);
                }

                result = new ActionResult { Success = true, Message = "Sensitive request approved and processed." };
            }
            else
            {
                var serviceMessage = ToServiceMessage(_pendingActionMessage!);
                result = await ExecuteIntentWithProgressAsync(serviceMessage);
                AddTimelineItem(serviceMessage.ExecutedIntent ?? "unknown", serviceMessage.IntentParameters, result.Success, result.Message);
            }


            // Replace the preview message with a result message that keeps the code artifacts
            if (result.Success && _pendingActionMessage != null && Messages.Contains(_pendingActionMessage))
            {
                var idx = Messages.IndexOf(_pendingActionMessage);
                Messages.Remove(_pendingActionMessage);

                var resultMsg = new UIChatMessage
                {
                    Content = result.Message,
                    IsUser = false,
                    MessageKind = "result"
                };

                // Carry over code artifacts so the code viewer stays visible
                foreach (var artifact in _pendingActionMessage.Artifacts)
                {
                    artifact.IsPreviewOnly = false;
                    resultMsg.Artifacts.Add(artifact);
                }

                if (idx >= 0 && idx <= Messages.Count)
                    Messages.Insert(idx, resultMsg);
                else
                    Messages.Add(resultMsg);
            }
            else
            {
                Messages.Add(new UIChatMessage
                {
                    Content = result.Success ? $"Action completed: {result.Message}" : $"Action failed: {result.Message}",
                    IsUser = false,
                    MessageKind = result.Success ? "result" : "error"
                });
            }

            if (QueueFollowUpConfirmation(result))
            {
                _lastExecutionSummary = result.Message.Length > 180 ? result.Message[..180] + "..." : result.Message;
                _pendingSensitiveUserMessage = null;
                _pendingSensitiveAttachments.Clear();
                SaveSessionHistory();
                RefreshUsageProperties();
                return;
            }

            _aiService.AddSystemNote($"[Confirmed action: {_pendingActionMessage?.Intent ?? "unknown"}] Result: {(result.Success ? "SUCCESS" : "FAILED")} - {result.Message}");
            _lastExecutionSummary = result.Message.Length > 180 ? result.Message[..180] + "..." : result.Message;
            _pendingActionMessage = null;
            _pendingSensitiveUserMessage = null;
            _pendingSensitiveAttachments.Clear();
            SaveSessionHistory();
            RefreshUsageProperties();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during execution");
            Messages.Add(new UIChatMessage { Content = $"Execution error: {ex.Message}", IsUser = false, MessageKind = "error" });
        }
        finally
        {
            IsTyping = false;
        }
    }

    private void OnCancel()
    {
        if (IsTyping && _activeExecutionCts != null)
        {
            _activeExecutionCts.Cancel();
            Messages.Add(new UIChatMessage
            {
                Content = "Stopping current action...",
                IsUser = false,
                MessageKind = "progress"
            });
            return;
        }

        ShowingConfirmation = false;
        ConfirmationMessage = string.Empty;
        _pendingActionMessage = null;
        _pendingSensitiveUserMessage = null;
        _pendingSensitiveAttachments.Clear();
    }

    // ── Phase 3: Plan Mode methods ──────────────────────────────────────

    private void PopulatePlanCard(ExecutionPlan plan)
    {
        _currentPlan = plan;
        PlanTitle = plan.Title;
        PlanSummary = plan.Summary;
        PlanOverallRisk = plan.OverallRisk switch
        {
            "risky" => "🔴 High Risk",
            "caution" => "🟡 Medium Risk",
            _ => "🟢 Low Risk"
        };
        PlanProgress = $"0/{plan.Steps.Count} steps";

        PlanSteps.Clear();
        foreach (var step in plan.Steps)
            PlanSteps.Add(PlanStepViewModel.FromStep(step));

        ShowingPlan = true;
    }

    private async Task OnApprovePlanAsync()
    {
        if (_currentPlan == null) return;

        IsExecutingPlan = true;
        _currentPlan.IsApproved = true;
        var sessionId = Guid.NewGuid().ToString("N")[..12];
        var totalFiles = 0;

        var preNote = _workflowNotes.EmitPreAction(
            _currentPlan.Title, _currentPlan.Steps.Count,
            _currentPlan.TotalEstimatedFiles, _currentPlan.OverallRisk);
        Messages.Add(new UIChatMessage { Content = preNote, IsUser = false, MessageKind = "plan-progress" });

        // Save checkpoint for resume
        var checkpoint = WorkflowCheckpoint.FromPlan(_currentPlan, sessionId);
        await _checkpointService.SaveCheckpointAsync(checkpoint, CancellationToken.None);

        try
        {
            PlanStepItem? step;
            while ((step = _currentPlan.GetNextReadyStep()) != null)
            {
                step.Status = PlanStepStatus.InProgress;
                step.StartedAt = DateTime.UtcNow;
                UpdateStepVm(step.Id, PlanStepStatus.InProgress);

                var completedCount = _currentPlan.Steps.Count(s => s.Status is PlanStepStatus.Completed or PlanStepStatus.Skipped);
                PlanProgress = $"{completedCount}/{_currentPlan.Steps.Count} steps";

                var progressNote = _workflowNotes.EmitProgress(
                    completedCount, _currentPlan.Steps.Count, totalFiles, step.Description);
                Messages.Add(new UIChatMessage { Content = progressNote, IsUser = false, MessageKind = "plan-progress" });

                var execResult = await _retryService.ExecuteWithRetryAsync(
                    sessionId, step,
                    async (attempt, ct) =>
                    {
                        // Execute via the orchestrator's normal code generation pipeline
                        var turnResult = await _aiService.ProcessAssistantTurnAsync(new AssistantTurnRequest
                        {
                            UserMessage = $"[Plan step {step.Id}] {step.Description} — Target: {step.Target}",
                            WorkspaceRoot = WorkspaceRoot,
                            LastCreatedFilePath = _lastCreatedFilePath,
                            LastExecutionSummary = _lastExecutionSummary,
                            RecentMessages = _aiService.ConversationHistory.ToList()
                        }, ct);

                        if (!string.IsNullOrWhiteSpace(turnResult.Content))
                            return StepExecutionResult.Ok(turnResult.Content, turnResult.Artifacts.Select(a => a.FilePath).Where(p => !string.IsNullOrWhiteSpace(p)).ToList());
                        return StepExecutionResult.Failure("No response from AI service.");
                    },
                    notesProgress: new Progress<string>(note =>
                    {
                        Messages.Add(new UIChatMessage { Content = note, IsUser = false, MessageKind = "plan-progress" });
                    }),
                    ct: CancellationToken.None);

                if (execResult.Success)
                {
                    totalFiles += execResult.FilesCreated.Count;
                    UpdateStepVm(step.Id, PlanStepStatus.Completed);
                    checkpoint.RecordStepCompleted(step.Id, execResult.FilesCreated);
                }
                else
                {
                    UpdateStepVm(step.Id, PlanStepStatus.Failed);
                    checkpoint.RecordStepFailed(step.Id);

                    if (execResult.RequiresEscalation)
                    {
                        Messages.Add(new UIChatMessage
                        {
                            Content = execResult.EscalationMessage,
                            IsUser = false,
                            MessageKind = "plan-escalation"
                        });
                        // Stop execution, user decides next action via skip/retry/abort
                        break;
                    }
                }

                await _checkpointService.SaveCheckpointAsync(checkpoint, CancellationToken.None);
            }

            // Check completion
            if (_currentPlan.IsComplete)
            {
                var successNote = _workflowNotes.EmitSuccess(
                    _currentPlan.Title, totalFiles, 0.85,
                    new[] { "Review generated files", "Run build to verify", "Test the new features" });
                Messages.Add(new UIChatMessage { Content = successNote, IsUser = false, MessageKind = "plan-complete" });
                _checkpointService.CompleteCheckpoint(sessionId);
                DismissPlanCard();
            }
            else
            {
                var completedCount = _currentPlan.Steps.Count(s => s.Status is PlanStepStatus.Completed or PlanStepStatus.Skipped);
                PlanProgress = $"{completedCount}/{_currentPlan.Steps.Count} steps";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Plan execution error");
            Messages.Add(new UIChatMessage
            {
                Content = $"Plan execution error: {ex.Message}\n\nReply **retry** to continue or **abort** to cancel.",
                IsUser = false,
                MessageKind = "error"
            });
        }
        finally
        {
            IsExecutingPlan = false;
            SaveSessionHistory();
            RefreshUsageProperties();
        }
    }

    private void OnRejectPlan()
    {
        _orchestrator.ClearPendingPlan();
        Messages.Add(new UIChatMessage
        {
            Content = $"Plan \"{PlanTitle}\" rejected. No actions were taken.",
            IsUser = false,
            MessageKind = "plan-rejected"
        });
        DismissPlanCard();
        SaveSessionHistory();
    }

    private async Task OnSkipStepAsync()
    {
        if (_currentPlan == null) return;

        var current = _currentPlan.GetNextReadyStep();
        if (current != null)
        {
            current.Status = PlanStepStatus.Skipped;
            current.ResultSummary = "Skipped by user";
            UpdateStepVm(current.Id, PlanStepStatus.Skipped);

            var completedCount = _currentPlan.Steps.Count(s => s.Status is PlanStepStatus.Completed or PlanStepStatus.Skipped);
            PlanProgress = $"{completedCount}/{_currentPlan.Steps.Count} steps";

            if (_currentPlan.IsComplete)
            {
                Messages.Add(new UIChatMessage
                {
                    Content = "All steps completed or skipped. Plan finished.",
                    IsUser = false,
                    MessageKind = "plan-complete"
                });
                DismissPlanCard();
            }
        }
        await Task.CompletedTask;
    }

    private void DismissPlanCard()
    {
        ShowingPlan = false;
        _currentPlan = null;
        _orchestrator.ClearPendingPlan();
        PlanSteps.Clear();
        PlanTitle = string.Empty;
        PlanSummary = string.Empty;
        PlanOverallRisk = string.Empty;
        PlanProgress = string.Empty;
    }

    private void UpdateStepVm(int stepId, PlanStepStatus status)
    {
        var vm = PlanSteps.FirstOrDefault(s => s.Id == stepId);
        vm?.UpdateStatus(status);
    }

    private IReadOnlyList<string> GetWorkspaceFiles()
    {
        if (string.IsNullOrWhiteSpace(WorkspaceRoot) || !Directory.Exists(WorkspaceRoot))
            return Array.Empty<string>();

        try
        {
            return Directory.EnumerateFiles(WorkspaceRoot, "*", SearchOption.AllDirectories)
                .Where(p => !p.Contains($@"\bin\") && !p.Contains($@"\obj\") && !p.Contains($@"\.git\") && !p.Contains($@"\.vs\"))
                .Take(150)
                .Select(p => Path.GetRelativePath(WorkspaceRoot, p))
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    // ── Resume / Checkpoint methods ────────────────────────────────────

    private async Task CheckForResumableWorkflowAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(WorkspaceRoot)) return;
            var checkpoint = await _checkpointService.FindResumableCheckpointAsync(WorkspaceRoot, CancellationToken.None);
            if (checkpoint == null) return;

            _resumableCheckpoint = checkpoint;
            ResumeSummary = $"\"{checkpoint.PlanTitle}\" — {checkpoint.CompletedStepIds.Count}/{checkpoint.TotalSteps} steps done, " +
                            $"{checkpoint.FailedStepIds.Count} failed. Pick up where you left off?";
            ShowingResume = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check for resumable workflows");
        }
    }

    private async Task OnResumeWorkflowAsync()
    {
        if (_resumableCheckpoint == null) return;

        var note = _workflowNotes.EmitResumed(
            _resumableCheckpoint.PlanTitle,
            _resumableCheckpoint.CompletedStepIds.Count,
            _resumableCheckpoint.TotalSteps);
        Messages.Add(new UIChatMessage { Content = note, IsUser = false, MessageKind = "plan-resumed" });

        ShowingResume = false;

        // Populate a lightweight plan card showing the checkpoint state
        PlanTitle = _resumableCheckpoint.PlanTitle;
        PlanSummary = $"Resumed — {_resumableCheckpoint.CompletedStepIds.Count} of {_resumableCheckpoint.TotalSteps} steps already completed.";
        PlanOverallRisk = "resumed";
        PlanProgress = $"{_resumableCheckpoint.CompletedStepIds.Count}/{_resumableCheckpoint.TotalSteps} steps";

        PlanSteps.Clear();
        for (int i = 0; i < _resumableCheckpoint.TotalSteps; i++)
        {
            var stepId = i + 1;
            var status = _resumableCheckpoint.CompletedStepIds.Contains(stepId) ? PlanStepStatus.Completed
                       : _resumableCheckpoint.FailedStepIds.Contains(stepId) ? PlanStepStatus.Failed
                       : PlanStepStatus.Pending;
            var vm = new PlanStepViewModel
            {
                Id = stepId,
                Description = $"Step {stepId}",
                Target = _resumableCheckpoint.CompletedStepIds.Contains(stepId) ? "completed" : "pending",
                RiskBadge = status.ToString().ToLowerInvariant()
            };
            vm.UpdateStatus(status);
            PlanSteps.Add(vm);
        }

        ShowingPlan = true;
        _resumableCheckpoint = null;
        await Task.CompletedTask;
    }

    private void OnDiscardResume()
    {
        if (_resumableCheckpoint != null)
        {
            _checkpointService.CompleteCheckpoint(_resumableCheckpoint.SessionId);
            _resumableCheckpoint = null;
        }
        ResumeSummary = string.Empty;
        ShowingResume = false;
    }

    // ── End Plan Mode methods ───────────────────────────────────────────

    private string FixPythonIndentation(string code)
    {
        var lines = code.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var sb = new StringBuilder();
        var indentStack = new Stack<int>();
        indentStack.Push(0);

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                sb.AppendLine();
                continue;
            }

            // Dedent keywords that close a block
            if (indentStack.Count > 1 &&
                (trimmed.StartsWith("else:") || trimmed.StartsWith("elif ")
                 || trimmed.StartsWith("except") || trimmed.StartsWith("finally:")
                 || trimmed.StartsWith("except:")))
            {
                if (indentStack.Count > 1) indentStack.Pop();
            }

            int currentIndent = indentStack.Peek();
            sb.AppendLine(new string(' ', currentIndent) + trimmed);

            // Lines that open a new block (end with ':')
            if (trimmed.EndsWith(':')
                && (trimmed.StartsWith("def ") || trimmed.StartsWith("class ")
                    || trimmed.StartsWith("if ") || trimmed.StartsWith("elif ")
                    || trimmed.StartsWith("else:") || trimmed.StartsWith("for ")
                    || trimmed.StartsWith("while ") || trimmed.StartsWith("with ")
                    || trimmed.StartsWith("try:") || trimmed.StartsWith("except")
                    || trimmed.StartsWith("finally:")))
            {
                indentStack.Push(currentIndent + 4);
            }
        }

        return sb.ToString();
    }

    private void OnActionCard(object? parameter)
    {
        var command = parameter?.ToString();
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        DismissActionCards();
        InputText = command;
        OnSend(null);
    }

    private void DismissActionCards()
    {
        ShowActionCards = false;
        _preferencesService.Update(p => p.HasDismissedActionCards = true);
    }

    private void OnPasteImage()
    {
        var attachment = _clipboardAttachmentService.TryCreateImageAttachmentFromClipboard();
        if (attachment == null)
        {
            return;
        }

        PendingAttachments.Add(UIAttachmentItem.FromAssistantAttachment(attachment));
        RaiseCommandStates();
    }

    private void OnAttachImageFromFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select an image",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            AddDroppedImage(dialog.FileName);
        }
    }

    private void OnAttachFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select a file",
            Filter = "All supported files (*.pdf;*.txt;*.csv;*.json;*.xml;*.md;*.log;*.html;*.htm;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.pdf;*.txt;*.csv;*.json;*.xml;*.md;*.log;*.html;*.htm;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|PDF files (*.pdf)|*.pdf|Text files (*.txt;*.csv;*.json;*.xml;*.md;*.log)|*.txt;*.csv;*.json;*.xml;*.md;*.log|Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            AddDroppedImage(dialog.FileName);
        }
    }

    private void OnRemovePendingAttachment(object? parameter)
    {
        if (parameter is UIAttachmentItem attachment)
        {
            PendingAttachments.Remove(attachment);
            RaiseCommandStates();
        }
    }

    private void OnSelectArtifact(object? parameter)
    {
        if (parameter is UIArtifactItem artifact)
        {
            SelectedArtifact = artifact;
        }
    }

    private void OnCopyArtifactCode(object? parameter)
    {
        if (parameter is string code && !string.IsNullOrWhiteSpace(code))
        {
            try
            {
                System.Windows.Clipboard.SetText(code);
            }
            catch { /* ignore clipboard failures */ }
        }
    }

    private async Task AutoExecuteIntentAsync(ChatMessage aiMessage)
    {
        try
        {
            IsTyping = true;
            var result = await ExecuteIntentWithProgressAsync(aiMessage);

            // Clear preview artifacts from the original AI message since the action is now complete
            if (result.Success)
            {
                var previewMessage = Messages.LastOrDefault(m => !m.IsUser && m.Artifacts.Count > 0 && m.Artifacts.Any(a => a.IsPreviewOnly));
                previewMessage?.Artifacts.Clear();
            }

            Messages.Add(new UIChatMessage
            {
                Content = result.Success ? $"Action completed: {result.Message}" : $"Action failed: {result.Message}",
                IsUser = false,
                MessageKind = result.Success ? "result" : "error"
            });

            if (QueueFollowUpConfirmation(result))
            {
                _lastExecutionSummary = result.Message.Length > 180 ? result.Message[..180] + "..." : result.Message;
                SaveSessionHistory();
                return;
            }

            AddTimelineItem(aiMessage.ExecutedIntent ?? "unknown", aiMessage.IntentParameters, result.Success, result.Message);
            _lastExecutionSummary = result.Message.Length > 180 ? result.Message[..180] + "..." : result.Message;
            SaveSessionHistory();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto execution failed");
            Messages.Add(new UIChatMessage { Content = $"Action failed: {ex.Message}", IsUser = false, MessageKind = "error" });
        }
        finally
        {
            IsTyping = false;
        }
    }

    private async Task<ActionResult> ExecuteIntentWithProgressAsync(ChatMessage message)
    {
        var progressMessage = new UIChatMessage
        {
            Content = "Working on it...",
            IsUser = false,
            MessageKind = "progress"
        };
        Messages.Add(progressMessage);

        var progress = new Progress<ActionProgress>(state =>
        {
            var content = state.Percent >= 0 && state.Percent <= 100
                ? $"{state.Icon} {state.Status}{Environment.NewLine}{BuildProgressBar(state.Percent)} {state.Percent:F0}%"
                : $"{state.Icon} {state.Status}";
            progressMessage.Content = content;
        });

        using var cts = new CancellationTokenSource();
        _activeExecutionCts = cts;
        RaiseCommandStates();
        _executionService.SetProgressReporter(progress);

        try
        {
            return await _aiService.ExecuteIntentAsync(message, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return new ActionResult { Success = false, Message = "⚠️ Action canceled by user." };
        }
        finally
        {
            _executionService.SetProgressReporter(null);
            Messages.Remove(progressMessage);
            _activeExecutionCts = null;
            RaiseCommandStates();
        }
    }

    private bool QueueFollowUpConfirmation(ActionResult result)
    {
        if (!result.RequiresConfirmation || string.IsNullOrWhiteSpace(result.FollowUpIntent))
            return false;

        var followUpMessage = new UIChatMessage
        {
            Content = string.IsNullOrWhiteSpace(result.FollowUpMessage)
                ? result.Message
                : result.FollowUpMessage,
            IsUser = false,
            MessageKind = "preview",
            Intent = result.FollowUpIntent,
            RequiresConfirmation = true,
            ConfirmationMessage = result.FollowUpConfirmationMessage,
            Parameters = new Dictionary<string, object>(result.FollowUpParameters, StringComparer.OrdinalIgnoreCase),
            Timestamp = DateTime.Now
        };

        Messages.Add(followUpMessage);
        _pendingActionMessage = followUpMessage;
        ConfirmationMessage = BuildExecutionPreview(followUpMessage, followUpMessage.ConfirmationMessage);
        ShowingConfirmation = true;
        return true;
    }

    private void OnClearChat()
    {
        Messages.Clear();
        ArtifactPaneItems.Clear();
        PendingAttachments.Clear();
        SelectedArtifact = null;
        _aiService.ClearHistory();
        ResetWelcomeMessage();
        ShowWelcome = true;

        var newSession = new ChatSessionItem
        {
            Id = Guid.NewGuid().ToString(),
            Title = "Current Session",
            LastUpdated = DateTime.Now,
            Preview = string.Empty
        };
        SelectedChat = newSession;
        PastChats.Insert(0, newSession);
        TrimPastChats();
        SaveSessionHistory();
        OnPropertyChanged(nameof(HasArtifacts));
    }

    private void OnApplyQuickCommand(object? parameter)
    {
        var text = parameter?.ToString();
        if (!string.IsNullOrWhiteSpace(text))
        {
            InputText = text;
        }
    }

    private async Task<bool> TryHandleLocalCommandAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        if (string.Equals(input, "/clear", StringComparison.OrdinalIgnoreCase)
            || string.Equals(input, "clear chat", StringComparison.OrdinalIgnoreCase))
        {
            OnClearChat();
            return true;
        }

        const string workspacePrefix = "/workspace ";
        if (input.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase))
        {
            WorkspaceRoot = input[workspacePrefix.Length..].Trim();
            Messages.Add(new UIChatMessage
            {
                Content = string.IsNullOrWhiteSpace(WorkspaceRoot)
                    ? "Workspace root cleared."
                    : $"Workspace root set to {WorkspaceRoot}",
                IsUser = false,
                MessageKind = "system"
            });
            SaveSessionHistory();
            return true;
        }

        if (string.Equals(input, "/paste", StringComparison.OrdinalIgnoreCase))
        {
            OnPasteImage();
            return true;
        }

        await Task.CompletedTask;
        return false;
    }

    private async Task<bool> TryHandleFollowUpAsync(string input)
    {
        if (!ShowingConfirmation)
        {
            return false;
        }

        var normalized = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "yes" or "y" or "confirm" or "approve")
        {
            OnExecute(null);
            await Task.CompletedTask;
            return true;
        }

        if (normalized is "no" or "n" or "cancel" or "stop")
        {
            OnCancel();
            Messages.Add(new UIChatMessage
            {
                Content = "Pending action canceled.",
                IsUser = false,
                MessageKind = "system"
            });
            await Task.CompletedTask;
            return true;
        }

        return false;
    }

    private UIChatMessage CreateUiMessage(ChatMessage aiResponse)
    {
        var displayContent = aiResponse.Content;

        // Strip fenced code blocks from message when CodePreview artifacts exist,
        // since the CodeArtifactViewer already displays the code separately.
        if (aiResponse.Artifacts.Any(a => a.Kind == AssistantArtifactKind.CodePreview))
        {
            displayContent = StripFencedCodeBlocks(displayContent);
        }

        var message = new UIChatMessage
        {
            Id = aiResponse.Id,
            Content = displayContent,
            IsUser = false,
            Timestamp = aiResponse.Timestamp,
            TokenCost = aiResponse.TokenCost,
            Intent = aiResponse.ExecutedIntent,
            RequiresConfirmation = aiResponse.RequiredConfirmation,
            ConfirmationMessage = aiResponse.ConfirmationMessage,
            Parameters = new Dictionary<string, object>(aiResponse.IntentParameters, StringComparer.OrdinalIgnoreCase),
            IsSensitiveRequest = aiResponse.IsSensitiveContentRequest,
            MessageKind = MapMessageKind(aiResponse),
            ToolTraceSummary = aiResponse.ToolTraceSummary,
            TurnMode = aiResponse.TurnMode,
            WorkspaceRoot = WorkspaceRoot,
            CodeSession = aiResponse.CodeSession
        };

        foreach (var attachment in aiResponse.Attachments)
        {
            message.Attachments.Add(UIAttachmentItem.FromAssistantAttachment(attachment));
        }

        foreach (var artifact in aiResponse.Artifacts)
        {
            message.Artifacts.Add(UIArtifactItem.FromArtifact(artifact));
        }

        foreach (var tool in aiResponse.ToolInvocations)
        {
            message.ToolInvocations.Add(UIToolTraceItem.FromTool(tool));
        }

        return message;
    }

    private static string StripFencedCodeBlocks(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var sb = new StringBuilder();
        var lines = text.Split('\n');
        bool insideFence = false;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```"))
            {
                insideFence = !insideFence;
                continue;
            }

            if (!insideFence)
            {
                sb.AppendLine(line);
            }
        }

        // Remove trailing "Code:" or "Code :" labels left behind
        var result = sb.ToString().TrimEnd();
        if (result.EndsWith("Code:", StringComparison.OrdinalIgnoreCase)
            || result.EndsWith("**Code:**", StringComparison.OrdinalIgnoreCase)
            || result.EndsWith("**Code**:", StringComparison.OrdinalIgnoreCase))
        {
            var idx = result.LastIndexOf('\n');
            result = idx >= 0 ? result[..idx].TrimEnd() : result;
        }

        return result;
    }

    private static string MapMessageKind(ChatMessage aiResponse)
    {
        if (aiResponse.TurnMode == AssistantTurnMode.Code)
        {
            return "code";
        }

        if (aiResponse.TurnMode == AssistantTurnMode.Vision)
        {
            return "vision";
        }

        if (aiResponse.TurnMode == AssistantTurnMode.CodeReview)
        {
            return "code_review";
        }

        if (aiResponse.TurnMode == AssistantTurnMode.Refactor)
        {
            return "refactor";
        }

        if (aiResponse.TurnMode == AssistantTurnMode.Research)
        {
            return "research";
        }

        if (aiResponse.TurnMode == AssistantTurnMode.Document)
        {
            return "document";
        }

        if (aiResponse.RequiredConfirmation)
        {
            return "preview";
        }

        return "assistant";
    }

    private static string BuildCodeEngineProgressContent(CodeGenerationStageEvent evt)
    {
        var header = $"{GetStageIcon(evt.Stage)} {evt.Stage}";
        var iterationText = evt.Iteration > 0 ? $"Iteration {evt.Iteration}" : "Pipeline";
        var filesText = evt.GeneratedFileCount > 0 ? $"{evt.GeneratedFileCount} file(s)" : "Preparing files";
        var confidenceText = evt.ConfidenceScore > 0 ? $"Confidence {evt.ConfidenceScore:P0}" : "Confidence pending";

        if (evt.Percent >= 0 && evt.Percent <= 100)
        {
            return $"{header}{Environment.NewLine}{evt.Message}{Environment.NewLine}{iterationText} • {filesText} • {confidenceText}{Environment.NewLine}{BuildProgressBar(evt.Percent)} {evt.Percent:F0}%";
        }

        return $"{header}{Environment.NewLine}{evt.Message}{Environment.NewLine}{iterationText} • {filesText} • {confidenceText}";
    }

    private static string GetStageIcon(CodeGenerationStage stage) => stage switch
    {
        CodeGenerationStage.Understanding => "🧠",
        CodeGenerationStage.Planning => "🗺️",
        CodeGenerationStage.Generation => "🛠️",
        CodeGenerationStage.Verification => "🔍",
        CodeGenerationStage.Improvement => "♻️",
        CodeGenerationStage.Finalization => "✅",
        _ => "⏳"
    };

    private void UpdateArtifactPane(UIChatMessage assistantMessage)
    {
        foreach (var artifact in assistantMessage.Artifacts)
        {
            var existing = ArtifactPaneItems.FirstOrDefault(item => item.Id == artifact.Id);
            if (existing != null)
            {
                continue;
            }

            ArtifactPaneItems.Add(new UIArtifactItem
            {
                Id = artifact.Id,
                Title = artifact.Title,
                Summary = artifact.Summary,
                Content = artifact.Content,
                SecondaryContent = artifact.SecondaryContent,
                Language = artifact.Language,
                FilePath = artifact.FilePath,
                Kind = artifact.Kind,
                IsPreviewOnly = artifact.IsPreviewOnly
            });
        }

        if (SelectedArtifact == null)
        {
            SelectedArtifact = ArtifactPaneItems.LastOrDefault();
        }

        OnPropertyChanged(nameof(HasArtifacts));
    }

    private void UpdateLastCreatedFilePath(UIChatMessage assistantMessage)
    {
        var fileArtifact = assistantMessage.Artifacts.LastOrDefault(item => !string.IsNullOrWhiteSpace(item.FilePath));
        if (fileArtifact != null)
        {
            _lastCreatedFilePath = fileArtifact.FilePath;
        }
    }

    private void LoadSessionHistory()
    {
        var preferences = _preferencesService.Get();
        PastChats.Clear();
        foreach (var session in preferences.ChatSessions.OrderByDescending(item => item.LastUpdated))
        {
            PastChats.Add(new ChatSessionItem
            {
                Id = session.Id,
                Title = session.Title,
                LastUpdated = session.LastUpdated,
                Preview = session.Preview
            });
        }

        var selected = PastChats.FirstOrDefault();
        if (selected == null)
        {
            selected = new ChatSessionItem { Id = Guid.NewGuid().ToString(), Title = "Current Session", LastUpdated = DateTime.Now };
            PastChats.Insert(0, selected);
        }

        SelectedChat = selected;
        if (preferences.ChatSessions.Any(item => item.Id == selected.Id && item.Messages.Count > 0))
        {
            LoadSessionById(selected.Id);
        }
        else
        {
            ResetWelcomeMessage();
            ShowWelcome = true;
        }
    }

    private void SaveSessionHistory()
    {
        var selected = GetOrCreateSelectedSession();
        var records = Messages.Select(message => new ChatMessageRecord
        {
            Content = message.Content,
            IsUser = message.IsUser,
            Timestamp = message.Timestamp,
            MessageKind = message.MessageKind,
            AttachmentIds = message.Attachments.Select(item => item.Id).ToList(),
            ArtifactIds = message.Artifacts.Select(item => item.Id).ToList(),
            ToolTraceSummary = message.ToolTraceSummary,
            WorkspaceRoot = message.WorkspaceRoot
        }).ToList();

        selected.LastUpdated = DateTime.Now;
        selected.Preview = Messages.LastOrDefault(item => item.IsUser)?.Content ?? Messages.LastOrDefault()?.Content ?? string.Empty;

        _preferencesService.Update(preferences =>
        {
            var existing = preferences.ChatSessions.FirstOrDefault(item => item.Id == selected.Id);
            if (existing == null)
            {
                existing = new ChatSessionHistoryRecord { Id = selected.Id };
                preferences.ChatSessions.Insert(0, existing);
            }

            existing.Title = selected.Title;
            existing.LastUpdated = selected.LastUpdated;
            existing.Preview = selected.Preview;
            existing.Messages = records;
        });

        LoadSessionListPreview(selected);
    }

    private void LoadSessionListPreview(ChatSessionItem selected)
    {
        var existing = PastChats.FirstOrDefault(item => item.Id == selected.Id);
        if (existing == null)
        {
            PastChats.Insert(0, selected);
        }
        else
        {
            existing.Title = selected.Title;
            existing.LastUpdated = selected.LastUpdated;
            existing.Preview = selected.Preview;
            var index = PastChats.IndexOf(existing);
            if (index > 0)
            {
                PastChats.Move(index, 0);
            }
        }

        TrimPastChats();
        OnPropertyChanged(nameof(PastChats));
    }

    private void LoadSmartSuggestions()
    {
        QuickCommands.Clear();
        var builtIns = new[]
        {
            "Review this code and suggest improvements",
            "Explain the screenshot I pasted",
            "Create a new WPF view with a modern layout",
            "Generate a diff for the selected file",
            "Organize my Downloads folder",
            "Help me debug this error"
        };

        foreach (var command in builtIns)
        {
            QuickCommands.Add(command);
        }

        foreach (var usage in _preferencesService.Get().CommandUsages.OrderByDescending(item => item.UseCount).Take(4))
        {
            if (!QuickCommands.Contains(usage.CommandText))
            {
                QuickCommands.Add(usage.CommandText);
            }
        }
    }

    private void UpdateSessionTitle()
    {
        var selected = GetOrCreateSelectedSession();
        var firstUserMessage = Messages.FirstOrDefault(item => item.IsUser)?.Content;
        if (!string.IsNullOrWhiteSpace(firstUserMessage))
        {
            selected.Title = firstUserMessage.Length > 52 ? firstUserMessage[..52] + "..." : firstUserMessage;
        }

        selected.LastUpdated = DateTime.Now;
        selected.Preview = Messages.LastOrDefault(item => item.IsUser)?.Content ?? Messages.LastOrDefault()?.Content ?? string.Empty;
    }

    private void RecordCommandUsage(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return;
        }

        _preferencesService.Update(preferences =>
        {
            var existing = preferences.CommandUsages.FirstOrDefault(item => item.CommandText.Equals(commandText, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                preferences.CommandUsages.Add(new CommandUsageRecord
                {
                    CommandText = commandText,
                    UseCount = 1,
                    LastUsed = DateTime.Now
                });
            }
            else
            {
                existing.UseCount += 1;
                existing.LastUsed = DateTime.Now;
            }
        });
    }

    private void RefreshUsageProperties()
    {
        OnPropertyChanged(nameof(RemainingTokens));
        OnPropertyChanged(nameof(UsedTokens));
        OnPropertyChanged(nameof(DailyTokenLimit));
        OnPropertyChanged(nameof(TokenTier));
        OnPropertyChanged(nameof(IsInputEnabled));
        OnPropertyChanged(nameof(ShowUpgradeMessage));
    }

    private void RaiseCommandStates()
    {
        (SendCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ExecuteCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (UndoLastActionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (PasteImageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(IsInputEnabled));
    }

    private void AddTimelineItem(string intent, Dictionary<string, object> parameters, bool success, string message)
    {
        ActionTimeline.Insert(0, new ActionTimelineItem
        {
            Timestamp = DateTime.Now,
            Intent = intent,
            Success = success,
            ResultPreview = message,
            Parameters = new Dictionary<string, object>(parameters, StringComparer.OrdinalIgnoreCase)
        });
    }

    private async Task UndoLastActionAsync()
    {
        if (_lastActionableMessage == null)
        {
            return;
        }

        await Task.CompletedTask;
        Messages.Add(new UIChatMessage
        {
            Content = "Undo preview is not available for this action yet. Review the last diff or revert manually.",
            IsUser = false,
            MessageKind = "system"
        });
    }

    private static bool IsActionableIntent(string? intent)
    {
        return !string.IsNullOrWhiteSpace(intent)
            && !string.Equals(intent, "chat", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(intent, "respond", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(intent, "answer", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(intent, "privacy_confirmation", StringComparison.OrdinalIgnoreCase);
    }

    private static ChatMessage CloneChatMessage(ChatMessage source)
    {
        return new ChatMessage
        {
            Id = source.Id,
            Content = source.Content,
            Role = source.Role,
            Timestamp = source.Timestamp,
            ExecutedIntent = source.ExecutedIntent,
            ExecutionResult = source.ExecutionResult,
            TokenCost = source.TokenCost,
            RequiredConfirmation = source.RequiredConfirmation,
            ConfirmationMessage = source.ConfirmationMessage,
            IntentParameters = new Dictionary<string, object>(source.IntentParameters, StringComparer.OrdinalIgnoreCase),
            IsSensitiveContentRequest = source.IsSensitiveContentRequest,
            TurnMode = source.TurnMode,
            ToolTraceSummary = source.ToolTraceSummary,
            CodeSession = source.CodeSession,
            Artifacts = source.Artifacts.Select(ToAssistantArtifact).ToList(),
            Attachments = source.Attachments.Select(ToAssistantAttachment).ToList(),
            ToolInvocations = source.ToolInvocations.Select(tool => new ToolInvocation
            {
                Name = tool.Name,
                Summary = tool.Summary,
                Status = tool.Status
            }).ToList()
        };
    }

    private static AssistantArtifact ToAssistantArtifact(AssistantArtifact artifact) => new()
    {
        Id = artifact.Id,
        Title = artifact.Title,
        Summary = artifact.Summary,
        Content = artifact.Content,
        SecondaryContent = artifact.SecondaryContent,
        Language = artifact.Language,
        FilePath = artifact.FilePath,
        Kind = artifact.Kind,
        IsPreviewOnly = artifact.IsPreviewOnly
    };

    private static AssistantAttachment ToAssistantAttachment(AssistantAttachment attachment) => new()
    {
        Id = attachment.Id,
        Kind = attachment.Kind,
        Title = attachment.Title,
        LocalPath = attachment.LocalPath,
        MimeType = attachment.MimeType,
        PreviewText = attachment.PreviewText,
        OcrText = attachment.OcrText,
        IsCloudReady = attachment.IsCloudReady,
        Width = attachment.Width,
        Height = attachment.Height,
        CreatedAt = attachment.CreatedAt
    };

    private static AssistantArtifact ToAssistantArtifact(UIArtifactItem artifact) => new()
    {
        Id = artifact.Id,
        Title = artifact.Title,
        Summary = artifact.Summary,
        Content = artifact.Content,
        SecondaryContent = artifact.SecondaryContent,
        Language = artifact.Language,
        FilePath = artifact.FilePath,
        Kind = Enum.TryParse<AssistantArtifactKind>(artifact.Kind, true, out var kind) ? kind : AssistantArtifactKind.CodePreview,
        IsPreviewOnly = artifact.IsPreviewOnly
    };

    private static AssistantAttachment ToAssistantAttachment(UIAttachmentItem attachment) => attachment.ToAssistantAttachment();

    private ChatMessage ToServiceMessage(UIChatMessage message)
    {
        return new ChatMessage
        {
            Content = message.Content,
            Role = "assistant",
            ExecutedIntent = message.Intent,
            RequiredConfirmation = message.RequiresConfirmation,
            ConfirmationMessage = message.ConfirmationMessage,
            IntentParameters = new Dictionary<string, object>(message.Parameters, StringComparer.OrdinalIgnoreCase),
            IsSensitiveContentRequest = message.IsSensitiveRequest,
            TurnMode = message.TurnMode,
            ToolTraceSummary = message.ToolTraceSummary,
            CodeSession = message.CodeSession,
            Artifacts = message.Artifacts.Select(ToAssistantArtifact).ToList(),
            Attachments = message.Attachments.Select(ToAssistantAttachment).ToList(),
            ToolInvocations = message.ToolInvocations.Select(item => new ToolInvocation
            {
                Name = item.Name,
                Summary = item.Summary,
                Status = Enum.TryParse<AssistantToolStatus>(item.Status, true, out var status) ? status : AssistantToolStatus.Completed
            }).ToList()
        };
    }

    private string BuildFallbackConfirmation(UIChatMessage message)
    {
        if (message.IsSensitiveRequest)
        {
            return "This may send sensitive file or image content to an external AI provider. Continue?";
        }

        if (message.Artifacts.Count > 0)
        {
            return "Review the generated preview in the artifact pane, then confirm to apply the change.";
        }

        var parameterSummary = message.Parameters.Count == 0
            ? "no parameters"
            : string.Join(", ", message.Parameters.Select(pair => $"{pair.Key}={pair.Value}"));
        return $"Execute action '{message.Intent}' with {parameterSummary}?";
    }

    private string BuildExecutionPreview(UIChatMessage message, string baseText)
    {
        var sb = new StringBuilder();
        sb.AppendLine(baseText);
        sb.AppendLine();
        sb.AppendLine($"Intent: {message.Intent}");
        sb.AppendLine($"Risk: {GetRiskLevel(message.Intent, message.Parameters)}");

        if (message.Artifacts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Artifacts ready:");
            foreach (var artifact in message.Artifacts.Take(4))
            {
                sb.AppendLine($"- {artifact.Title}: {artifact.Summary}");
            }

            var diffArtifact = message.Artifacts.FirstOrDefault(item => item.Kind.Equals(AssistantArtifactKind.Diff.ToString(), StringComparison.OrdinalIgnoreCase));
            if (diffArtifact != null)
            {
                sb.AppendLine();
                sb.AppendLine("Diff preview:");
                foreach (var line in diffArtifact.Content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Take(18))
                {
                    sb.AppendLine(line);
                }
            }
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine(BuildParameterDiff(message.Intent, message.Parameters));
            var generatedPreview = _executionService.GenerateActionPreview(message.Intent, message.Parameters);
            if (!string.IsNullOrWhiteSpace(generatedPreview))
            {
                sb.AppendLine();
                sb.AppendLine(generatedPreview);
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildParameterDiff(string? intent, Dictionary<string, object> parameters)
    {
        if (parameters.Count == 0)
        {
            return "No parameters provided.";
        }

        var lines = parameters.Select(pair => $"- {pair.Key}: {pair.Value}");
        return string.Join(Environment.NewLine, lines.Prepend($"Parameters for {intent}:"));
    }

    private static string GetRiskLevel(string? intent, Dictionary<string, object> parameters)
    {
        var normalized = intent?.ToLowerInvariant() ?? string.Empty;
        if (normalized.Contains("delete", StringComparison.Ordinal) || normalized.Contains("remove", StringComparison.Ordinal))
        {
            return "High";
        }

        if (normalized.Contains("edit", StringComparison.Ordinal) || normalized.Contains("move", StringComparison.Ordinal) || normalized.Contains("rename", StringComparison.Ordinal))
        {
            return "Medium";
        }

        if (parameters.Keys.Any(key => key.Contains("path", StringComparison.OrdinalIgnoreCase)))
        {
            return "Medium";
        }

        return "Low";
    }

    private static string BuildProgressBar(double percent)
    {
        var clamped = Math.Max(0, Math.Min(100, percent));
        var filled = (int)Math.Round(clamped / 10d, MidpointRounding.AwayFromZero);
        return $"[{new string('#', filled)}{new string('-', 10 - filled)}]";
    }

    private ChatSessionItem GetOrCreateSelectedSession()
    {
        if (SelectedChat != null)
        {
            return SelectedChat;
        }

        SelectedChat = new ChatSessionItem
        {
            Id = Guid.NewGuid().ToString(),
            Title = "Current Session",
            LastUpdated = DateTime.Now,
            Preview = string.Empty
        };
        PastChats.Insert(0, SelectedChat);
        return SelectedChat;
    }

    private void ResetWelcomeMessage()
    {
        Messages.Clear();
        Messages.Add(new UIChatMessage
        {
            Content = "Hello. I can help with code, OCR, screenshots, and desktop actions. Paste an image or describe the change you want.",
            IsUser = false,
            MessageKind = "assistant"
        });
    }

    private void TrimPastChats()
    {
        while (PastChats.Count > 30)
        {
            PastChats.RemoveAt(PastChats.Count - 1);
        }
    }

    private void OnLoadChat(object? parameter)
    {
        if (parameter is ChatSessionItem session)
        {
            LoadSessionById(session.Id);
        }
        else if (parameter is string id && !string.IsNullOrWhiteSpace(id))
        {
            LoadSessionById(id);
        }
    }

    private void OnToggleChatMenu(object? parameter)
    {
        if (parameter is ChatSessionItem session)
        {
            // Close all other menus first
            foreach (var chat in PastChats)
            {
                if (chat != session) chat.IsMenuOpen = false;
            }
            session.IsMenuOpen = !session.IsMenuOpen;
        }
    }

    private void OnRenameChat(object? parameter)
    {
        if (parameter is not ChatSessionItem session) return;
        session.IsMenuOpen = false;

        // Set a placeholder name from the preview, user can edit via binding
        var newTitle = string.IsNullOrWhiteSpace(session.Preview)
            ? session.Title
            : session.Preview.Length > 40 ? session.Preview[..40] + "..." : session.Preview;
        session.Title = newTitle;

        _preferencesService.Update(preferences =>
        {
            var existing = preferences.ChatSessions.FirstOrDefault(item => item.Id == session.Id);
            if (existing != null) existing.Title = newTitle;
        });
    }

    private void OnDeleteChat(object? parameter)
    {
        if (parameter is not ChatSessionItem session) return;
        session.IsMenuOpen = false;

        _preferencesService.Update(preferences =>
        {
            var existing = preferences.ChatSessions.FirstOrDefault(item => item.Id == session.Id);
            if (existing != null) preferences.ChatSessions.Remove(existing);
        });

        PastChats.Remove(session);

        // If we deleted the currently selected chat, start a new one
        if (SelectedChat?.Id == session.Id)
        {
            OnClearChat();
        }
    }
}
