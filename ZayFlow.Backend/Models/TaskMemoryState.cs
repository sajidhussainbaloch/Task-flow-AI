namespace ZayFlow.Backend.Models;

public sealed class TaskMemoryState
{
    public List<string> RecentExecutedCommands { get; set; } = new();
    public Dictionary<string, int> FrequentFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? PreferredOrganizationStyle { get; set; }
    public string? LastTargetFolder { get; set; }
    public IntentCategory LastIntent { get; set; } = IntentCategory.Unknown;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
