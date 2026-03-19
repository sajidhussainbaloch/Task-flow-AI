using Microsoft.Extensions.Logging;
using ZayFlow.Backend.Contracts;
using ZayFlow.Backend.DTOs;
using ZayFlow.Backend.Models;
using ZayFlow.Core.Abstractions;

namespace ZayFlow.Backend.Services;

public sealed class SuggestionService : ISuggestionService
{
    private readonly IFileScanner _fileScanner;
    private readonly IHeuristicRuleEngine _heuristics;
    private readonly IPathSafetyValidator _pathSafety;
    private readonly ILogger<SuggestionService> _logger;

    public SuggestionService(
        IFileScanner fileScanner,
        IHeuristicRuleEngine heuristics,
        IPathSafetyValidator pathSafety,
        ILogger<SuggestionService> logger)
    {
        _fileScanner = fileScanner ?? throw new ArgumentNullException(nameof(fileScanner));
        _heuristics = heuristics ?? throw new ArgumentNullException(nameof(heuristics));
        _pathSafety = pathSafety ?? throw new ArgumentNullException(nameof(pathSafety));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<SuggestionDTO>> GetSuggestionsAsync(string? targetFolder = null, CancellationToken cancellationToken = default)
    {
        var resolvedFolder = string.IsNullOrWhiteSpace(targetFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            : Path.GetFullPath(targetFolder);

        if (!_pathSafety.IsPathAllowed(resolvedFolder) || !Directory.Exists(resolvedFolder))
        {
            return new[]
            {
                new SuggestionDTO("Safety", "No safe folder available for suggestions.", "Medium", "organize downloads")
            };
        }

        var files = await _fileScanner.ScanDirectoryAsync(resolvedFolder, cancellationToken).ConfigureAwait(false);
        var intent = new IntentResult
        {
            Intent = IntentCategory.OrganizeFiles,
            OriginalInput = "suggest",
            NormalizedInput = "suggest",
            TargetFolderPath = resolvedFolder,
            Confidence = 1,
            Reason = "Suggestion analysis"
        };

        return BuildSuggestionsFromInsight(resolvedFolder, _heuristics.Analyze(resolvedFolder, files, intent));
    }

    public async Task<IReadOnlyList<SuggestionDTO>> GetSuggestionsForIntentAsync(IntentResult intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);

        var targetFolder = intent.TargetFolderPath;
        if (string.IsNullOrWhiteSpace(targetFolder) || !_pathSafety.IsPathAllowed(targetFolder) || !Directory.Exists(targetFolder))
        {
            return new[]
            {
                new SuggestionDTO("Context", "Specify a valid safe folder to receive contextual suggestions.", "Low", "organize downloads")
            };
        }

        var files = await _fileScanner.ScanDirectoryAsync(targetFolder, cancellationToken).ConfigureAwait(false);
        var insight = _heuristics.Analyze(targetFolder, files, intent);
        return BuildSuggestionsFromInsight(targetFolder, insight);
    }

    private IReadOnlyList<SuggestionDTO> BuildSuggestionsFromInsight(string targetFolder, HeuristicInsight insight)
    {
        var suggestions = new List<SuggestionDTO>();

        if (insight.RecommendGroupByType)
        {
            suggestions.Add(new SuggestionDTO(
                "Organization",
                $"{targetFolder} contains {insight.FileCount} files. Organizing by file type is recommended.",
                "Low",
                "organize downloads"));
        }

        if (insight.RecommendArchiveLargeFolder)
        {
            suggestions.Add(new SuggestionDTO(
                "Optimization",
                $"Folder size is {insight.TotalBytes / (1024 * 1024)} MB. Archive older files to reduce clutter.",
                "Medium",
                "archive old files"));
        }

        if (insight.RecommendDeduplication)
        {
            suggestions.Add(new SuggestionDTO(
                "Cleanup",
                $"Detected {insight.DuplicateNameCount} duplicate file-name groups. Deduplication is recommended.",
                "Medium",
                "clean duplicates"));
        }

        if (insight.RecommendArchiveOldFiles)
        {
            suggestions.Add(new SuggestionDTO(
                "Retention",
                $"Detected {insight.OldFileCount} old files. Consider moving them to an archive folder.",
                "Low",
                "archive old files"));
        }

        if (suggestions.Count == 0)
        {
            suggestions.Add(new SuggestionDTO(
                "Healthy",
                "No urgent issues detected. Folder is already in good shape.",
                "Info",
                null));
        }

        _logger.LogInformation("Generated {Count} offline suggestions for folder {Folder}", suggestions.Count, targetFolder);
        return suggestions;
    }
}
