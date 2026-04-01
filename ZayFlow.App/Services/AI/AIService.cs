using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Services;
using ZayFlow.App.Services.AI.Contracts;
using ZayFlow.App.Services.AI.Providers;
using ZayFlow.App.Services.Assistant;

namespace ZayFlow.App.Services.AI;

/// <summary>
/// Main AI service that orchestrates Cloudflare-backed assistant turns, token management, and execution.
/// </summary>
public class AIService : IAIService
{
    private readonly Dictionary<string, IAIProvider> _providers;
    private IAIProvider? _currentProvider;
    private readonly ObservableCollection<ChatMessage> _conversationHistory;
    private readonly IntentExecutionService _executionService;
    private readonly IAppPreferencesService _preferencesService;
    private readonly IActionAuditService _auditService;
    private readonly IAssistantOrchestrator _assistantOrchestrator;
    private readonly ILogger<AIService> _logger;

    public string CurrentProvider => _currentProvider?.ProviderName ?? "None";
    public IReadOnlyList<ChatMessage> ConversationHistory => _conversationHistory.AsReadOnly();
    public int TokensUsedToday => _preferencesService.Get().TokensUsedToday;

    public int DailyTokenLimit
    {
        get
        {
            var pref = _preferencesService.Get();
            return pref.IsPremium ? pref.PremiumDailyLimit : pref.FreeDailyLimit;
        }
    }

    public int RemainingTokens => Math.Max(0, DailyTokenLimit - TokensUsedToday);
    public bool CanUseAI => RemainingTokens > 0;
    public string TokenTier => _preferencesService.Get().IsPremium ? "Premium" : "Free";

    public AIService(
        CloudflareProvider cloudflareProvider,
        IntentExecutionService executionService,
        IAppPreferencesService preferencesService,
        IActionAuditService auditService,
        IAssistantOrchestrator assistantOrchestrator,
        ILogger<AIService> logger)
    {
        _logger = logger;
        _conversationHistory = new ObservableCollection<ChatMessage>();
        _providers = new Dictionary<string, IAIProvider>();
        _executionService = executionService;
        _preferencesService = preferencesService;
        _auditService = auditService;
        _assistantOrchestrator = assistantOrchestrator;

        RegisterProvider(cloudflareProvider);

        var preferences = _preferencesService.Get();
        if (!string.IsNullOrWhiteSpace(preferences.CloudflareApiToken)
            && !string.IsNullOrWhiteSpace(preferences.CloudflareAccountId))
        {
            cloudflareProvider.Initialize(BuildCloudflareConfig(preferences));
            _logger.LogInformation("Cloudflare provider initialized");
        }

        SwitchProvider("Cloudflare");
    }

    public void RegisterProvider(IAIProvider provider)
    {
        _providers[provider.ProviderName] = provider;
        if (_currentProvider == null)
        {
            _currentProvider = provider;
            _logger.LogInformation("Provider registered and set as current: {Provider}", provider.ProviderName);
        }
    }

    public void SwitchProvider(string providerName)
    {
        if (_providers.TryGetValue(providerName, out var provider))
        {
            _currentProvider = provider;
            _preferencesService.Update(p => p.SelectedProvider = providerName);
            _logger.LogInformation("Switched to provider: {Provider}", providerName);
        }
        else
        {
            _logger.LogWarning("Provider not found: {Provider}", providerName);
        }
    }

    public List<string> GetAvailableProviders() => _providers.Keys.ToList();

    public Task<ChatMessage> ProcessUserMessageAsync(string userMessage, bool bypassPrivacyConfirmation = false, CancellationToken ct = default)
    {
        var preferences = _preferencesService.Get();
        return ProcessAssistantTurnAsync(new AssistantTurnRequest
        {
            UserMessage = userMessage,
            PrivacyMode = preferences.PrivacyMode,
            BypassPrivacyConfirmation = bypassPrivacyConfirmation,
            WorkspaceRoot = preferences.DefaultWorkspaceRoot,
            UseLocalOcrFirst = preferences.UseLocalOcrFirst,
            RecentMessages = ConversationHistory.ToList()
        }, ct);
    }

    public async Task<ChatMessage> ProcessAssistantTurnAsync(AssistantTurnRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserMessage))
        {
            return new ChatMessage { Content = "Please enter a message.", Role = "assistant" };
        }

        if (_currentProvider == null)
        {
            return new ChatMessage { Content = "No AI provider configured. Please configure Cloudflare in settings.", Role = "assistant" };
        }

        if (!CanUseAI)
        {
            return new ChatMessage { Content = "You have no tokens remaining. Please upgrade to continue using AI features.", Role = "assistant" };
        }

        var preferences = _preferencesService.Get();
        if (!request.BypassPrivacyConfirmation && preferences.PrivacyMode && LooksSensitive(request))
        {
            var privacyMessage = new ChatMessage
            {
                Content = "Privacy mode is enabled. This request may send file or image content externally. Confirm to continue.",
                Role = "assistant",
                RequiredConfirmation = true,
                ConfirmationMessage = "Send potentially sensitive file, image, or clipboard content to the AI provider?",
                IsSensitiveContentRequest = true,
                ExecutedIntent = "privacy_confirmation",
                Attachments = request.Attachments.ToList(),
                TurnMode = request.Attachments.Count > 0 ? AssistantTurnMode.Vision : AssistantTurnMode.Chat
            };

            _conversationHistory.Add(privacyMessage);
            await _auditService.LogAsync("PRIVACY", "Blocked sensitive request until explicit confirmation", ct);
            return privacyMessage;
        }

        var userMsg = new ChatMessage
        {
            Content = request.UserMessage,
            Role = "user",
            Attachments = request.Attachments.ToList(),
            TurnMode = request.Attachments.Count > 0 ? AssistantTurnMode.Vision : AssistantTurnMode.Chat
        };
        _conversationHistory.Add(userMsg);

        if (!TryDeductTokens(1))
        {
            return new ChatMessage { Content = "Insufficient tokens for AI call.", Role = "assistant" };
        }

        request.RecentMessages = _conversationHistory.ToList();
        var turn = await _assistantOrchestrator.ProcessTurnAsync(request, ct).ConfigureAwait(false);
        turn.TokenCost = NormalizeAiTokenCost(turn.TokenCost, request);

        var additionalCost = Math.Max(0, turn.TokenCost - 1);
        if (additionalCost > 0 && !TryDeductTokens(additionalCost))
        {
            turn.Message = "Insufficient tokens for this request size.";
            turn.Intent = "chat";
            turn.RequiresConfirmation = false;
            turn.TokenCost = 1;
        }

        var assistantMessage = new ChatMessage
        {
            Content = turn.Message,
            Role = "assistant",
            ExecutedIntent = turn.Intent,
            TokenCost = turn.TokenCost,
            RequiredConfirmation = turn.RequiresConfirmation,
            ConfirmationMessage = turn.ConfirmationMessage,
            IntentParameters = turn.Parameters,
            IsSensitiveContentRequest = turn.IsSensitiveContentRequest,
            TurnMode = turn.Mode,
            ToolTraceSummary = turn.ToolTraceSummary,
            Artifacts = turn.Artifacts,
            Attachments = turn.Attachments,
            ToolInvocations = turn.ToolInvocations,
            CodeSession = turn.CodeSession,
            RawCodeSession = turn.RawCodeSession
        };

        _conversationHistory.Add(assistantMessage);
        await _auditService.LogAsync("AI", $"Provider={CurrentProvider}; Intent={turn.Intent}; Tokens={turn.TokenCost}; Mode={turn.Mode}", ct);
        _logger.LogInformation("Processed assistant turn - Intent: {Intent}, Tokens: {Tokens}, Mode: {Mode}", turn.Intent, turn.TokenCost, turn.Mode);

        return assistantMessage;
    }

    public async Task<ActionResult> ExecuteIntentAsync(ChatMessage assistantMessage, CancellationToken ct = default)
    {
        if (assistantMessage == null)
        {
            return new ActionResult { Success = false, Message = "No action to execute" };
        }

        if (string.IsNullOrWhiteSpace(assistantMessage.ExecutedIntent) || assistantMessage.ExecutedIntent == "chat")
        {
            return new ActionResult { Success = true, Message = "No executable action in this response" };
        }

        if (!TryDeductTokens(1))
        {
            return new ActionResult { Success = false, Message = "Insufficient tokens for execution" };
        }

        var response = new AIResponse
        {
            Success = true,
            Intent = assistantMessage.ExecutedIntent,
            Message = assistantMessage.Content,
            Parameters = assistantMessage.IntentParameters,
            RequiresConfirmation = assistantMessage.RequiredConfirmation,
            ConfirmationMessage = assistantMessage.ConfirmationMessage,
            TokenCost = 1
        };

        var result = await _executionService.ExecuteIntentAsync(response, ct);
        assistantMessage.ExecutionResult = result;
        await _auditService.LogAsync("EXECUTION", $"Intent={response.Intent}; Success={result.Success}; {result.Message}", ct);
        return result;
    }

    public async Task<string> SummarizeFileAsync(string fileName, string fileContent, CancellationToken ct = default)
    {
        if (_currentProvider == null)
        {
            return "AI service unavailable. Please configure a provider in Settings.";
        }

        if (!CanUseAI || !TryDeductTokens(1))
        {
            return "No tokens available. Please upgrade or wait for token reset.";
        }

        var summary = await _currentProvider.SummarizeFileAsync(fileName, fileContent, ct);
        await _auditService.LogAsync("AI", $"Provider={CurrentProvider}; Intent=summarize_file; Tokens=1", ct);
        return summary;
    }

    public async Task<string> GenerateTextAsync(string prompt, CancellationToken ct = default)
    {
        if (_currentProvider == null)
        {
            return "AI service unavailable. Please configure a provider in Settings.";
        }

        if (!CanUseAI || !TryDeductTokens(1))
        {
            return "No tokens available. Please upgrade or wait for token reset.";
        }

        var text = await _currentProvider.GenerateTextAsync(prompt, ct);
        await _auditService.LogAsync("AI", $"Provider={CurrentProvider}; Intent=generate_text; Tokens=1", ct);
        return text;
    }

    public async Task<ImageGenerationResult> GenerateImageAsync(string prompt, string savePath, CancellationToken ct = default)
    {
        if (_currentProvider is not CloudflareProvider orProvider)
        {
            return new ImageGenerationResult { Success = false, Message = "Image generation requires the Cloudflare provider." };
        }

        if (!CanUseAI || !TryDeductTokens(3))
        {
            return new ImageGenerationResult { Success = false, Message = "Not enough tokens for image generation." };
        }

        var result = await orProvider.GenerateImageAsync(prompt, savePath, ct);
        await _auditService.LogAsync("AI", $"Provider={CurrentProvider}; Intent=generate_image; Tokens=3", ct);
        return result;
    }

    public void ClearHistory()
    {
        _conversationHistory.Clear();
        if (_currentProvider is CloudflareProvider orProvider)
        {
            orProvider.ClearConversationHistory();
        }

        _logger.LogInformation("Conversation history cleared (service + provider)");
    }

    public void AddSystemNote(string note)
    {
        if (_currentProvider is CloudflareProvider orProvider)
        {
            orProvider.AddNote(note);
        }

        _logger.LogInformation("System note added to AI memory: {Snippet}", note[..Math.Min(80, note.Length)]);
    }

    private bool TryDeductTokens(int amount)
    {
        if (amount <= 0)
        {
            return true;
        }

        var pref = _preferencesService.Get();
        var limit = pref.IsPremium ? pref.PremiumDailyLimit : pref.FreeDailyLimit;
        if (pref.TokensUsedToday + amount > limit)
        {
            return false;
        }

        _preferencesService.Update(p => p.TokensUsedToday += amount);
        return true;
    }

    private static int NormalizeAiTokenCost(int tokenCost, AssistantTurnRequest request)
    {
        var baseCost = tokenCost <= 0 ? 1 : tokenCost;
        if (request.Attachments.Count > 0)
        {
            return Math.Max(3, baseCost);
        }

        if (!string.IsNullOrWhiteSpace(request.WorkspaceRoot) || request.UserMessage.Contains("code", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Max(2, baseCost);
        }

        return Math.Max(1, baseCost);
    }

    private static bool LooksSensitive(AssistantTurnRequest request)
    {
        if (request.Attachments.Count > 0)
        {
            return true;
        }

        var lower = request.UserMessage.ToLowerInvariant();
        var asksRead = lower.Contains("read ") || lower.Contains("summar") || lower.Contains("extract") || lower.Contains("analy") || lower.Contains("rewrite") || lower.Contains("screenshot");
        var mentionsFile = lower.Contains(".txt") || lower.Contains(".pdf") || lower.Contains(".docx") || lower.Contains("c:\\") || lower.Contains("d:\\") || lower.Contains("file") || lower.Contains("image") || lower.Contains("clipboard");
        return asksRead && mentionsFile;
    }

    private static string BuildCloudflareConfig(AppPreferences preferences)
        => $"{preferences.CloudflareApiToken}|{preferences.CloudflareAccountId}|{preferences.PreferredChatModel}";
}
