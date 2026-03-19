using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Services;
using ZayFlow.App.Services.AI.Contracts;
using ZayFlow.App.Services.AI.Providers;

namespace ZayFlow.App.Services.AI;

/// <summary>
/// Main AI service that orchestrates AI provider, token management, and response formatting.
/// </summary>
public class AIService : IAIService
{
    private readonly Dictionary<string, IAIProvider> _providers;
    private IAIProvider? _currentProvider;
    private readonly ObservableCollection<ChatMessage> _conversationHistory;
    private readonly IntentExecutionService _executionService;
    private readonly IAppPreferencesService _preferencesService;
    private readonly IActionAuditService _auditService;
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
        GroqProvider groqProvider,
        IntentExecutionService executionService,
        IAppPreferencesService preferencesService,
        IActionAuditService auditService,
        ILogger<AIService> logger)
    {
        _logger = logger;
        _conversationHistory = new ObservableCollection<ChatMessage>();
        _providers = new Dictionary<string, IAIProvider>();
        _executionService = executionService;
        _preferencesService = preferencesService;
        _auditService = auditService;

        RegisterProvider(groqProvider);

        var preferences = _preferencesService.Get();

        // Initialize Groq
        if (!string.IsNullOrWhiteSpace(preferences.GroqApiKey))
        {
            groqProvider.Initialize(preferences.GroqApiKey);
            _logger.LogInformation("Groq provider initialized");
        }

        SwitchProvider("Groq (Free)");
    }

    public void RegisterProvider(IAIProvider provider)
    {
        _providers[provider.ProviderName] = provider;
        if (_currentProvider == null)
        {
            _currentProvider = provider;
            _logger.LogInformation($"Provider registered and set as current: {provider.ProviderName}");
        }
    }

    public void SwitchProvider(string providerName)
    {
        if (_providers.TryGetValue(providerName, out var provider))
        {
            _currentProvider = provider;
            _preferencesService.Update(p => p.SelectedProvider = providerName);
            _logger.LogInformation($"Switched to provider: {providerName}");
        }
        else
        {
            _logger.LogWarning($"Provider not found: {providerName}");
        }
    }

    public List<string> GetAvailableProviders() => _providers.Keys.ToList();

    public async Task<ChatMessage> ProcessUserMessageAsync(string userMessage, bool bypassPrivacyConfirmation = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return new ChatMessage
            {
                Content = "Please enter a message.",
                Role = "assistant"
            };
        }

        if (_currentProvider == null)
        {
            return new ChatMessage
            {
                Content = "No AI provider configured. Please configure DeepSeek API key in settings.",
                Role = "assistant"
            };
        }

        if (!CanUseAI)
        {
            return new ChatMessage
            {
                Content = "You have no tokens remaining. Please upgrade to continue using AI features.",
                Role = "assistant"
            };
        }

        var preferences = _preferencesService.Get();
        if (!bypassPrivacyConfirmation && preferences.PrivacyMode && LooksLikeSensitiveFileContentRequest(userMessage))
        {
            var privacyMessage = new ChatMessage
            {
                Content = "Privacy mode is enabled. This request may send file content externally. Confirm to continue.",
                Role = "assistant",
                RequiredConfirmation = true,
                ConfirmationMessage = "Send potentially sensitive file content to AI provider?",
                IsSensitiveContentRequest = true,
                ExecutedIntent = "privacy_confirmation"
            };

            _conversationHistory.Add(privacyMessage);
            await _auditService.LogAsync("PRIVACY", "Blocked sensitive request until explicit confirmation", ct);
            return privacyMessage;
        }

        // Add user message to history
        var userMsg = new ChatMessage
        {
            Content = userMessage,
            Role = "user"
        };
        _conversationHistory.Add(userMsg);

        if (!TryDeductTokens(1))
        {
            return new ChatMessage
            {
                Content = "Insufficient tokens for AI call.",
                Role = "assistant"
            };
        }

        // Get AI response
        var aiResponse = await _currentProvider.SendChatMessageAsync(userMessage, ct);
        aiResponse.TokenCost = NormalizeAiTokenCost(aiResponse, userMessage);

        var additionalCost = Math.Max(0, aiResponse.TokenCost - 1);
        if (additionalCost > 0 && !TryDeductTokens(additionalCost))
        {
            aiResponse.Success = false;
            aiResponse.Message = "Insufficient tokens for this request size.";
            aiResponse.Intent = "chat";
            aiResponse.RequiresConfirmation = false;
            aiResponse.TokenCost = 1;
        }

        // Create assistant message
        var assistantMsg = new ChatMessage
        {
            Content = aiResponse.Message,
            Role = "assistant",
            ExecutedIntent = aiResponse.Intent,
            TokenCost = aiResponse.TokenCost,
            RequiredConfirmation = aiResponse.RequiresConfirmation,
            ConfirmationMessage = aiResponse.ConfirmationMessage,
            IntentParameters = aiResponse.Parameters
        };

        _conversationHistory.Add(assistantMsg);

        await _auditService.LogAsync("AI", $"Provider={CurrentProvider}; Intent={aiResponse.Intent}; Tokens={aiResponse.TokenCost}", ct);

        _logger.LogInformation($"Processed message - Intent: {aiResponse.Intent}, Tokens: {aiResponse.TokenCost}");

        return assistantMsg;
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

    public void ClearHistory()
    {
        _conversationHistory.Clear();
        // Also clear the provider's conversation memory
        if (_currentProvider is GroqProvider groq)
        {
            groq.ClearConversationHistory();
        }
        _logger.LogInformation("Conversation history cleared (service + provider)");
    }

    /// <summary>
    /// Inject a system-level note into the provider's conversation memory.
    /// This lets the AI know about action results, so it can reference them.
    /// </summary>
    public void AddSystemNote(string note)
    {
        if (_currentProvider is GroqProvider groq)
        {
            groq.AddNote(note);
        }
        _logger.LogInformation($"System note added to AI memory: {note[..Math.Min(80, note.Length)]}");
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

    private static int NormalizeAiTokenCost(AIResponse response, string userMessage)
    {
        var baseCost = response.TokenCost <= 0 ? 1 : response.TokenCost;
        var intent = response.Intent?.Trim().ToLowerInvariant() ?? "chat";

        if (intent.Contains("summar") && (intent.Contains("file") || userMessage.Contains(".pdf", StringComparison.OrdinalIgnoreCase) || userMessage.Contains(".docx", StringComparison.OrdinalIgnoreCase)))
        {
            return Math.Max(2, baseCost);
        }

        if (response.Parameters.TryGetValue("fileSizeKb", out var fileSizeObj)
            && long.TryParse(fileSizeObj?.ToString(), out var fileSizeKb)
            && fileSizeKb > 512)
        {
            return Math.Max(2, baseCost);
        }

        return Math.Max(1, baseCost);
    }

    private static bool LooksLikeSensitiveFileContentRequest(string message)
    {
        var lower = message.ToLowerInvariant();
        var asksRead = lower.Contains("read ") || lower.Contains("summar") || lower.Contains("extract") || lower.Contains("analy") || lower.Contains("rewrite");
        var mentionsFile = lower.Contains(".txt") || lower.Contains(".pdf") || lower.Contains(".docx") || lower.Contains("c:\\") || lower.Contains("d:\\") || lower.Contains("file");
        return asksRead && mentionsFile;
    }
}
