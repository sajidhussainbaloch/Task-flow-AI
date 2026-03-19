using ZayFlow.Core.Domain.Models;

namespace ZayFlow.Core.Abstractions;

public interface IFileScanner
{
    Task<IReadOnlyList<FileMetadata>> ScanDirectoryAsync(string path, CancellationToken cancellationToken = default);
}
