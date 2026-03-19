namespace ZayFlow.Core.Domain.Models;

public sealed record FileMetadata
{
    public string Path { get; }
    public string Extension { get; }
    public long Size { get; }
    public bool IsSystemFolder { get; }
    public bool IsExecutable { get; }

    public FileMetadata(string path, string extension, long size, bool isSystemFolder, bool isExecutable)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Extension = extension ?? string.Empty;
        Size = size;
        IsSystemFolder = isSystemFolder;
        IsExecutable = isExecutable;
    }
}
