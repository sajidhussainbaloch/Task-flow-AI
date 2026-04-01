using ZayFlow.App.Services.Assistant;

namespace ZayFlow.App.Services.AI.Contracts;

/// <summary>
/// Main AI service that handles token management, provider switching, and response formatting.
/// </summary>
public interface IAIService
{
    Task<ChatMessage> ProcessUserMessageAsync(string userMessage, bool bypassPrivacyConfirmation = false, CancellationToken ct = default);

    Task<ChatMessage> ProcessAssistantTurnAsync(AssistantTurnRequest request, CancellationToken ct = default);

    Task<string> SummarizeFileAsync(string fileName, string fileContent, CancellationToken ct = default);

    Task<string> GenerateTextAsync(string prompt, CancellationToken ct = default);

    Task<ImageGenerationResult> GenerateImageAsync(string prompt, string savePath, CancellationToken ct = default);

    Task<ActionResult> ExecuteIntentAsync(ChatMessage assistantMessage, CancellationToken ct = default);

    IReadOnlyList<ChatMessage> ConversationHistory { get; }

    void ClearHistory();

    void AddSystemNote(string note);

    string CurrentProvider { get; }

    void SwitchProvider(string providerName);

    List<string> GetAvailableProviders();

    int TokensUsedToday { get; }
    int DailyTokenLimit { get; }
    int RemainingTokens { get; }
    bool CanUseAI { get; }
    string TokenTier { get; }
}

public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Content { get; set; } = string.Empty;
    public string Role { get; set; } = "user";
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string? ExecutedIntent { get; set; }
    public ActionResult? ExecutionResult { get; set; }
    public int TokenCost { get; set; } = 0;
    public bool RequiredConfirmation { get; set; }
    public string ConfirmationMessage { get; set; } = string.Empty;
    public Dictionary<string, object> IntentParameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsSensitiveContentRequest { get; set; }
    public AssistantTurnMode TurnMode { get; set; } = AssistantTurnMode.Chat;
    public string ToolTraceSummary { get; set; } = string.Empty;
    public List<AssistantArtifact> Artifacts { get; set; } = new();
    public List<AssistantAttachment> Attachments { get; set; } = new();
    public List<ToolInvocation> ToolInvocations { get; set; } = new();
    public CodeSessionInfo? CodeSession { get; set; }
    /// <summary>Phase 2 — The full code session for multi-turn continuation.</summary>
    public ZayFlow.App.Services.CodeGeneration.Contracts.CodeGenerationSession? RawCodeSession { get; set; }

    public bool IsUserMessage => Role == "user";
    public bool IsAssistantMessage => Role == "assistant";
}

public class ImageGenerationResult
{
    public bool Success { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
