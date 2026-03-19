using System.IO;

namespace ZayFlow.App.Services;

public interface IInsightsService
{
    Task<StorageInsights> GetInsightsAsync(string rootFolder, CancellationToken ct = default);
}

public sealed class StorageInsights
{
    public List<(string Folder, int Count)> MostUsedFolders { get; set; } = new();
    public List<string> LargestFiles { get; set; } = new();
    public int OldFilesCount { get; set; }
    public int DuplicateGroups { get; set; }
}

public sealed class InsightsService : IInsightsService
{
    public Task<StorageInsights> GetInsightsAsync(string rootFolder, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rootFolder))
                {
                    return new StorageInsights();
                }

                if (!Directory.Exists(rootFolder))
                {
                    return new StorageInsights();
                }

                var files = new List<FileInfo>();
                
                // Enumerate files with error handling for access denied
                try
                {
                    foreach (var filePath in Directory.EnumerateFiles(rootFolder, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            files.Add(new FileInfo(filePath));
                        }
                        catch (UnauthorizedAccessException) { /* Skip inaccessible files */ }
                        catch (FileNotFoundException) { /* Skip if file was deleted during scan */ }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    return new StorageInsights();
                }

                if (files.Count == 0)
                {
                    return new StorageInsights();
                }

                var mostUsed = files
                    .Where(f => f.DirectoryName != null)
                    .GroupBy(f => f.DirectoryName!)
                    .OrderByDescending(g => g.Count())
                    .Take(5)
                    .Select(g => (g.Key, g.Count()))
                    .ToList();

                var largest = files
                    .OrderByDescending(f => f.Length)
                    .Take(5)
                    .Select(f => $"{f.Name} ({FormatFileSize(f.Length)})")
                    .ToList();

                var sixMonthsAgo = DateTime.UtcNow.AddMonths(-6);
                var oldFiles = files.Count(f => f.LastAccessTimeUtc < sixMonthsAgo);
                
                var duplicateGroups = files
                    .Where(f => f.Length > 0)
                    .GroupBy(f => f.Length)
                    .Count(g => g.Count() > 1);

                return new StorageInsights
                {
                    MostUsedFolders = mostUsed,
                    LargestFiles = largest,
                    OldFilesCount = oldFiles,
                    DuplicateGroups = duplicateGroups
                };
            }
            catch (Exception)
            {
                return new StorageInsights();
            }
        }, ct);
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
