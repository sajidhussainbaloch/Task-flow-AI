namespace ZayFlow.Backend.Models;

public sealed class HeuristicInsight
{
    public int FileCount { get; init; }
    public long TotalBytes { get; init; }
    public int DuplicateNameCount { get; init; }
    public int OldFileCount { get; init; }
    public bool RecommendGroupByType { get; init; }
    public bool RecommendArchiveLargeFolder { get; init; }
    public bool RecommendDeduplication { get; init; }
    public bool RecommendArchiveOldFiles { get; init; }
    public IReadOnlyList<string> Suggestions { get; init; } = Array.Empty<string>();
}
