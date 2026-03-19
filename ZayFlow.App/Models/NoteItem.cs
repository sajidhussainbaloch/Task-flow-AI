namespace ZayFlow.App.Models;

public sealed class NoteItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Folder { get; set; } = "General";
    public string Title { get; set; } = "Untitled";
    public string Content { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
