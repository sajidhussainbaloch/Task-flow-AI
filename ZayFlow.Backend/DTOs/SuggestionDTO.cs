namespace ZayFlow.Backend.DTOs;

public sealed class SuggestionDTO
{
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "Info";
    public string? SuggestedCommand { get; set; }

    public SuggestionDTO()
    {
    }

    public SuggestionDTO(string category, string message, string severity, string? suggestedCommand)
    {
        Category = category;
        Message = message;
        Severity = severity;
        SuggestedCommand = suggestedCommand;
    }
}
