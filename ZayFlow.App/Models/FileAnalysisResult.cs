namespace ZayFlow.App.Models;

public sealed class FileAnalysisResult
{
    public string Summary { get; set; } = string.Empty;
    public string KeyPoints { get; set; } = string.Empty;
    public string SimplifiedVersion { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}
