using ZayFlow.Core.Domain.Enums;

namespace ZayFlow.Core.Domain.Models;

public sealed record ActionItem
{
    public ActionType Type { get; }
    public string SourcePath { get; }
    public string DestinationPath { get; }
    public long FileSizeBytes { get; }
    public bool IsSystemPath { get; }
    public bool IsExecutable { get; }

    public ActionItem(
        ActionType type,
        string sourcePath,
        string destinationPath,
        long fileSizeBytes,
        bool isSystemPath,
        bool isExecutable)
    {
        Type = type;
        SourcePath = sourcePath ?? throw new ArgumentNullException(nameof(sourcePath));
        DestinationPath = destinationPath ?? string.Empty;
        FileSizeBytes = fileSizeBytes;
        IsSystemPath = isSystemPath;
        IsExecutable = isExecutable;
    }
}
