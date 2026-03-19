namespace ZayFlow.App.Models;

public sealed class AutomationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public string DestinationFolder { get; set; } = string.Empty;
    public string Schedule { get; set; } = "Daily";
    public bool Enabled { get; set; } = true;
}
