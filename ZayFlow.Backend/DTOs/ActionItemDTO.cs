namespace ZayFlow.Backend.DTOs;

public sealed class ActionItemDTO
{
    public string Type { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public bool IsSystemPath { get; set; }
    public bool IsExecutable { get; set; }

    public ActionItemDTO()
    {
    }

    public ActionItemDTO(
        string type,
        string displayName,
        string sourcePath,
        string destinationPath,
        long fileSizeBytes,
        bool isSystemPath,
        bool isExecutable)
    {
        Type = type;
        DisplayName = displayName;
        SourcePath = sourcePath;
        DestinationPath = destinationPath;
        FileSizeBytes = fileSizeBytes;
        IsSystemPath = isSystemPath;
        IsExecutable = isExecutable;
    }
}
