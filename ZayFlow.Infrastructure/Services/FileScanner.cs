using Microsoft.Extensions.Logging;
using ZayFlow.Core.Abstractions;
using ZayFlow.Core.Domain.Models;

namespace ZayFlow.Infrastructure.Services;

public sealed class FileScanner : IFileScanner
{
    private readonly ILogger<FileScanner> _logger;

    public FileScanner(ILogger<FileScanner> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<FileMetadata>> ScanDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Scan path is required.", nameof(path));
        }

        var normalizedPath = NormalizePath(path);
        var result = new List<FileMetadata>();

        if (!Directory.Exists(normalizedPath))
        {
            _logger.LogWarning("Scan skipped because directory does not exist: {Path}", normalizedPath);
            return result;
        }

        await Task.Run(() =>
        {
            var pending = new Stack<string>();
            pending.Push(normalizedPath);

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var current = pending.Pop();
                var isSystemFolder = IsProtectedSystemPath(current);

                try
                {
                    foreach (var file in Directory.EnumerateFiles(current))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            var fileInfo = new FileInfo(file);
                            var extension = fileInfo.Extension;

                            result.Add(new FileMetadata(
                                fileInfo.FullName,
                                extension,
                                fileInfo.Exists ? fileInfo.Length : 0,
                                isSystemFolder,
                                IsExecutableExtension(extension)));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Could not inspect file metadata: {FilePath}", file);
                        }
                    }

                    foreach (var subDirectory in Directory.EnumerateDirectories(current))
                    {
                        pending.Push(subDirectory);
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogWarning(ex, "Access denied while scanning: {Path}", current);
                }
                catch (DirectoryNotFoundException ex)
                {
                    _logger.LogWarning(ex, "Directory disappeared during scan: {Path}", current);
                }
                catch (PathTooLongException ex)
                {
                    _logger.LogWarning(ex, "Path too long while scanning: {Path}", current);
                }
            }
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Directory scan completed. Path: {Path}, FileCount: {FileCount}", normalizedPath, result.Count);
        return result;
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path.Trim());
    }

    private static bool IsExecutableExtension(string extension)
    {
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".sys", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProtectedSystemPath(string path)
    {
        var normalized = NormalizePath(path);

        return normalized.StartsWith(NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.Windows)), StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)), StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)), StringComparison.OrdinalIgnoreCase);
    }
}
