namespace ZayFlow.Backend.Models;

public sealed class IntentResult
{
    public IntentCategory Intent { get; init; } = IntentCategory.Unknown;
    public string OriginalInput { get; init; } = string.Empty;
    public string NormalizedInput { get; init; } = string.Empty;
    public string? TargetFolderPath { get; init; }
    public string? FileTypeHint { get; init; }
    public string? RenamePattern { get; init; }
    public bool UseLastContext { get; init; }
    public double Confidence { get; init; }
    public string Reason { get; init; } = string.Empty;
}
