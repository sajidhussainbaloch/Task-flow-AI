namespace ZayFlow.App.Services.AI.Contracts;

/// <summary>
/// Abstraction for AI providers (DeepSeek, Local LLM, etc.)
/// Ensures we can switch providers without breaking the rest of the app.
/// </summary>
public interface IAIProvider
{
    string ProviderName { get; }
    
    /// <summary>
    /// Send a chat message to the AI and get structured response.
    /// </summary>
    Task<AIResponse> SendChatMessageAsync(string userMessage, CancellationToken ct = default);

    /// <summary>
    /// Summarize file content.
    /// </summary>
    Task<string> SummarizeFileAsync(string fileName, string fileContent, CancellationToken ct = default);

    /// <summary>
    /// Generate plain text content from a prompt.
    /// </summary>
    Task<string> GenerateTextAsync(string prompt, CancellationToken ct = default);
    
    /// <summary>
    /// Check if provider is ready and has valid credentials.
    /// </summary>
    bool IsConfigured { get; }
    
    /// <summary>
    /// Initialize the provider with configuration.
    /// </summary>
    void Initialize(string configKey);
}

/// <summary>
/// Structured response from AI provider.
/// </summary>
public class AIResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// Parsed intent from AI (rename_files, move_files, change_wallpaper, etc.)
    /// </summary>
    public string Intent { get; set; } = string.Empty;
    
    /// <summary>
    /// Structured parameters for the intent.
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();
    
    /// <summary>
    /// Confirmation message to show user before execution.
    /// </summary>
    public string ConfirmationMessage { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether execution requires user confirmation.
    /// </summary>
    public bool RequiresConfirmation { get; set; } = true;
    
    /// <summary>
    /// Token cost for this AI call.
    /// </summary>
    public int TokenCost { get; set; } = 1;
}
