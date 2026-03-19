namespace ZayFlow.App.Services.AI.Contracts;

/// <summary>
/// Main AI service that handles token management, provider switching, and response formatting.
/// </summary>
public interface IAIService
{
    /// <summary>
    /// Send message to AI and execute any required actions.
    /// </summary>
    Task<ChatMessage> ProcessUserMessageAsync(string userMessage, bool bypassPrivacyConfirmation = false, CancellationToken ct = default);

    Task<string> SummarizeFileAsync(string fileName, string fileContent, CancellationToken ct = default);

    Task<string> GenerateTextAsync(string prompt, CancellationToken ct = default);

    Task<ActionResult> ExecuteIntentAsync(ChatMessage assistantMessage, CancellationToken ct = default);
    
    /// <summary>
    /// Get conversation history.
    /// </summary>
    IReadOnlyList<ChatMessage> ConversationHistory { get; }
    
    /// <summary>
    /// Clear conversation history.
    /// </summary>
    void ClearHistory();

    /// <summary>
    /// Add a system note to conversation memory so AI knows about action results.
    /// </summary>
    void AddSystemNote(string note);
    
    /// <summary>
    /// Current AI provider name.
    /// </summary>
    string CurrentProvider { get; }
    
    /// <summary>
    /// Switch to different AI provider.
    /// </summary>
    void SwitchProvider(string providerName);
    
    /// <summary>
    /// Get available providers.
    /// </summary>
    List<string> GetAvailableProviders();

    int TokensUsedToday { get; }
    int DailyTokenLimit { get; }
    int RemainingTokens { get; }
    bool CanUseAI { get; }
    string TokenTier { get; }
}

/// <summary>
/// A single message in the conversation.
/// </summary>
public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Content { get; set; } = string.Empty;
    public string Role { get; set; } = "user"; // "user" or "assistant"
    public DateTime Timestamp { get; set; } = DateTime.Now;
    
    /// <summary>
    /// For assistant messages: the action executed.
    /// </summary>
    public string? ExecutedIntent { get; set; }
    
    /// <summary>
    /// For assistant messages: the result of execution.
    /// </summary>
    public ActionResult? ExecutionResult { get; set; }
    
    /// <summary>
    /// Token cost for this message.
    /// </summary>
    public int TokenCost { get; set; } = 0;
    
    /// <summary>
    /// Whether this message required user confirmation.
    /// </summary>
    public bool RequiredConfirmation { get; set; } = false;

    public string ConfirmationMessage { get; set; } = string.Empty;

    public Dictionary<string, object> IntentParameters { get; set; } = new();

    public bool IsSensitiveContentRequest { get; set; } = false;
    
    public bool IsUserMessage => Role == "user";
    public bool IsAssistantMessage => Role == "assistant";
}
