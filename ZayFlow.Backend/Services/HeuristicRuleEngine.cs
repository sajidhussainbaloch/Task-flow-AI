using ZayFlow.Backend.Contracts;
using ZayFlow.Backend.Models;
using ZayFlow.Core.Domain.Models;

namespace ZayFlow.Backend.Services;

public sealed class HeuristicRuleEngine : IHeuristicRuleEngine
{
    private const int LargeFolderFileThreshold = 100;
    private const long LargeFolderSizeThresholdBytes = 1024L * 1024L * 1024L;
    private const int OldFileThresholdMonths = 6;

    public HeuristicInsight Analyze(string targetFolder, IReadOnlyList<FileMetadata> files, IntentResult intent)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(intent);

        var fileCount = files.Count;
        var totalBytes = files.Sum(file => file.Size);

        var duplicateNameCount = files
            .GroupBy(file => Path.GetFileName(file.Path), StringComparer.OrdinalIgnoreCase)
            .Count(group => group.Count() > 1);

        var oldThresholdUtc = DateTime.UtcNow.AddMonths(-OldFileThresholdMonths);
        var oldFileCount = files.Count(file => TryGetLastWriteUtc(file.Path, out var modifiedUtc) && modifiedUtc < oldThresholdUtc);

        var suggestGroupByType = fileCount > LargeFolderFileThreshold;
        var suggestArchiveLarge = totalBytes > LargeFolderSizeThresholdBytes;
        var suggestDedup = duplicateNameCount > 0;
        var suggestArchiveOld = oldFileCount > 0;

        var suggestions = new List<string>();
        if (suggestGroupByType)
        {
            suggestions.Add($"Folder has {fileCount} files; grouping by type is recommended.");
        }

        if (suggestArchiveLarge)
        {
            suggestions.Add($"Folder size is {totalBytes / (1024 * 1024)} MB; consider archiving older content.");
        }

        if (suggestDedup)
        {
            suggestions.Add($"Detected {duplicateNameCount} duplicate file-name groups; deduplication is recommended.");
        }

        if (suggestArchiveOld)
        {
            suggestions.Add($"Detected {oldFileCount} files older than {OldFileThresholdMonths} months.");
        }

        if (intent.Intent == IntentCategory.Unknown)
        {
            suggestions.Add("Command is unclear; try commands like 'organize downloads' or 'archive old files'.");
        }

        return new HeuristicInsight
        {
            FileCount = fileCount,
            TotalBytes = totalBytes,
            DuplicateNameCount = duplicateNameCount,
            OldFileCount = oldFileCount,
            RecommendGroupByType = suggestGroupByType,
            RecommendArchiveLargeFolder = suggestArchiveLarge,
            RecommendDeduplication = suggestDedup,
            RecommendArchiveOldFiles = suggestArchiveOld,
            Suggestions = suggestions
        };
    }

    private static bool TryGetLastWriteUtc(string path, out DateTime lastWriteUtc)
    {
        try
        {
            lastWriteUtc = File.GetLastWriteTimeUtc(path);
            return true;
        }
        catch
        {
            lastWriteUtc = DateTime.MinValue;
            return false;
        }
    }
}
