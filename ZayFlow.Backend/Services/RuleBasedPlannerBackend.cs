using Microsoft.Extensions.Logging;
using ZayFlow.Backend.Contracts;
using ZayFlow.Backend.DTOs;
using ZayFlow.Backend.Mapping;
using ZayFlow.Backend.Models;
using ZayFlow.Core.Abstractions;
using ZayFlow.Core.Domain.Enums;
using ZayFlow.Core.Domain.Models;
using BackendPlanner = ZayFlow.Backend.Contracts.IPlanner;

namespace ZayFlow.Backend.Services;

public sealed class RuleBasedPlannerBackend : BackendPlanner
{
    private readonly IFileScanner _fileScanner;
    private readonly IIntentDetector _intentDetector;
    private readonly IHeuristicRuleEngine _heuristics;
    private readonly ITaskMemoryManager _taskMemory;
    private readonly IPathSafetyValidator _pathSafety;
    private readonly ILogger<RuleBasedPlannerBackend> _logger;

    public RuleBasedPlannerBackend(
        IFileScanner fileScanner,
        IIntentDetector intentDetector,
        IHeuristicRuleEngine heuristics,
        ITaskMemoryManager taskMemory,
        IPathSafetyValidator pathSafety,
        ILogger<RuleBasedPlannerBackend> logger)
    {
        _fileScanner = fileScanner ?? throw new ArgumentNullException(nameof(fileScanner));
        _intentDetector = intentDetector ?? throw new ArgumentNullException(nameof(intentDetector));
        _heuristics = heuristics ?? throw new ArgumentNullException(nameof(heuristics));
        _taskMemory = taskMemory ?? throw new ArgumentNullException(nameof(taskMemory));
        _pathSafety = pathSafety ?? throw new ArgumentNullException(nameof(pathSafety));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PlanDTO> GeneratePlanAsync(string userInput, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userInput))
        {
            throw new ArgumentException("Planner input cannot be empty.", nameof(userInput));
        }

        var intent = _intentDetector.DetectIntent(userInput);
        var resolvedTarget = await ResolveTargetFolderAsync(intent, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Intent detected. Intent={Intent}, Confidence={Confidence}, Target={Target}, Reason={Reason}",
            intent.Intent,
            intent.Confidence,
            resolvedTarget ?? "<none>",
            intent.Reason);

        if (intent.Intent is IntentCategory.Help or IntentCategory.Unknown)
        {
            var help = BuildHelpPlan(userInput, intent);
            await _taskMemory.RememberPlanningContextAsync(userInput, intent, resolvedTarget, null, cancellationToken).ConfigureAwait(false);
            return help;
        }

        if (intent.Intent == IntentCategory.OpenApplication)
        {
            var openAppPlan = new PlanDTO(
                Guid.NewGuid(),
                "Open application automation is intentionally disabled in offline-safe mode. Use file automation intents instead.",
                RiskLevel.Low,
                new List<ActionItemDTO>(),
                DateTime.UtcNow)
            {
                SourceCommand = userInput
            };

            await _taskMemory.RememberPlanningContextAsync(userInput, intent, resolvedTarget, null, cancellationToken).ConfigureAwait(false);
            return openAppPlan;
        }

        if (intent.Intent == IntentCategory.SystemInfo)
        {
            var target = resolvedTarget ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(target) || !_pathSafety.IsPathAllowed(target))
            {
                return new PlanDTO(Guid.NewGuid(), "System info request could not be completed for unsafe or missing path.", RiskLevel.Low, new List<ActionItemDTO>(), DateTime.UtcNow)
                {
                    SourceCommand = userInput
                };
            }

            var files = await _fileScanner.ScanDirectoryAsync(target, cancellationToken).ConfigureAwait(false);
            var insight = _heuristics.Analyze(target, files, intent);
            var description = $"Folder: {target} | Files: {insight.FileCount} | Size: {insight.TotalBytes / (1024 * 1024)} MB | Duplicates: {insight.DuplicateNameCount} | OldFiles: {insight.OldFileCount}";

            await _taskMemory.RememberPlanningContextAsync(userInput, intent, target, null, cancellationToken).ConfigureAwait(false);
            return new PlanDTO(Guid.NewGuid(), description, RiskLevel.Low, new List<ActionItemDTO>(), DateTime.UtcNow)
            {
                SourceCommand = userInput
            };
        }

        if (string.IsNullOrWhiteSpace(resolvedTarget))
        {
            return new PlanDTO(
                Guid.NewGuid(),
                "No folder context found. Specify a folder (e.g. downloads/desktop) or run a command first then say 'organize it again'.",
                RiskLevel.Low,
                new List<ActionItemDTO>(),
                DateTime.UtcNow)
            {
                SourceCommand = userInput
            };
        }

        _pathSafety.EnsurePathAllowed(resolvedTarget);

        if (!Directory.Exists(resolvedTarget))
        {
            return new PlanDTO(
                Guid.NewGuid(),
                $"Target directory does not exist: {resolvedTarget}",
                RiskLevel.Low,
                new List<ActionItemDTO>(),
                DateTime.UtcNow)
            {
                SourceCommand = userInput
            };
        }

        var scannedFiles = await _fileScanner.ScanDirectoryAsync(resolvedTarget, cancellationToken).ConfigureAwait(false);
        var filteredFiles = FilterByFileTypeHint(scannedFiles, intent.FileTypeHint);
        var insightReport = _heuristics.Analyze(resolvedTarget, filteredFiles, intent);

        var actions = BuildActions(intent, resolvedTarget, filteredFiles, insightReport);
        var risk = InferRisk(actions, intent.Intent);

        var summaryParts = new List<string>
        {
            $"Intent: {intent.Intent}",
            $"Target: {resolvedTarget}",
            $"Actions: {actions.Count}"
        };

        if (insightReport.RecommendGroupByType)
        {
            summaryParts.Add("Recommended grouping by type");
        }

        if (insightReport.RecommendDeduplication)
        {
            summaryParts.Add($"Duplicate groups: {insightReport.DuplicateNameCount}");
        }

        var plan = new Plan(Guid.NewGuid(), string.Join(" | ", summaryParts), actions, risk, DateTime.UtcNow);
        var dto = BackendMapper.ToDto(plan);
        dto.SourceCommand = userInput;

        var style = insightReport.RecommendGroupByType ? "ByType" : "ByDate";
        await _taskMemory.RememberPlanningContextAsync(userInput, intent, resolvedTarget, style, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Offline intelligent plan generated. Intent={Intent}, Target={Target}, ActionCount={ActionCount}, Risk={Risk}",
            intent.Intent,
            resolvedTarget,
            dto.Actions.Count,
            dto.RiskLevel);

        return dto;
    }

    private async Task<string?> ResolveTargetFolderAsync(IntentResult intent, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(intent.TargetFolderPath))
        {
            return Path.GetFullPath(intent.TargetFolderPath);
        }

        if (intent.UseLastContext)
        {
            var remembered = await _taskMemory.ResolveLastTargetFolderAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(remembered))
            {
                return remembered;
            }
        }

        return intent.Intent switch
        {
            IntentCategory.CleanTemp => Path.GetTempPath(),
            IntentCategory.OrganizeFiles => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            IntentCategory.ArchiveOldFiles => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            IntentCategory.BatchRename => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            _ => null
        };
    }

    private static IReadOnlyList<FileMetadata> FilterByFileTypeHint(IReadOnlyList<FileMetadata> files, string? fileTypeHint)
    {
        if (string.IsNullOrWhiteSpace(fileTypeHint))
        {
            return files;
        }

        var extensionSet = fileTypeHint.ToLowerInvariant() switch
        {
            "images" => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tiff" },
            "documents" => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf", ".doc", ".docx", ".txt", ".xlsx", ".pptx", ".csv" },
            "videos" => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mkv", ".avi", ".mov", ".wmv" },
            "audio" => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".flac", ".aac", ".m4a" },
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };

        if (extensionSet.Count == 0)
        {
            return files;
        }

        return files.Where(file => extensionSet.Contains(file.Extension)).ToList();
    }

    private static List<ActionItem> BuildActions(IntentResult intent, string targetFolder, IReadOnlyList<FileMetadata> files, HeuristicInsight insight)
    {
        return intent.Intent switch
        {
            IntentCategory.OrganizeFiles => BuildOrganizeActions(targetFolder, files, intent.FileTypeHint),
            IntentCategory.CleanTemp => BuildTempCleanupActions(files),
            IntentCategory.ArchiveOldFiles => BuildArchiveActions(targetFolder, files),
            IntentCategory.BatchRename => BuildRenameActions(files, intent.RenamePattern),
            _ => new List<ActionItem>()
        };
    }

    private static List<ActionItem> BuildOrganizeActions(string targetFolder, IReadOnlyList<FileMetadata> files, string? fileTypeHint)
    {
        var actions = new List<ActionItem>(files.Count);

        foreach (var file in files)
        {
            var extension = string.IsNullOrWhiteSpace(file.Extension)
                ? "NoExtension"
                : file.Extension.TrimStart('.').ToUpperInvariant();

            var categoryRoot = string.IsNullOrWhiteSpace(fileTypeHint)
                ? "By Type"
                : fileTypeHint.Equals("images", StringComparison.OrdinalIgnoreCase)
                    ? "Images"
                    : fileTypeHint.Equals("documents", StringComparison.OrdinalIgnoreCase)
                        ? "Documents"
                        : fileTypeHint.Equals("videos", StringComparison.OrdinalIgnoreCase)
                            ? "Videos"
                            : fileTypeHint.Equals("audio", StringComparison.OrdinalIgnoreCase)
                                ? "Audio"
                                : "By Type";

            var destinationDirectory = Path.Combine(targetFolder, categoryRoot, extension);
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(file.Path));

            if (string.Equals(file.Path, destinationPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            actions.Add(new ActionItem(ActionType.Move, file.Path, destinationPath, file.Size, file.IsSystemFolder, file.IsExecutable));
        }

        return actions;
    }

    private static List<ActionItem> BuildTempCleanupActions(IReadOnlyList<FileMetadata> files)
    {
        var allowedTempExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".tmp", ".temp", ".log", ".bak", ".old"
        };

        return files
            .Where(file => allowedTempExtensions.Contains(file.Extension))
            .Select(file => new ActionItem(ActionType.Delete, file.Path, string.Empty, file.Size, file.IsSystemFolder, file.IsExecutable))
            .ToList();
    }

    private static List<ActionItem> BuildArchiveActions(string targetFolder, IReadOnlyList<FileMetadata> files)
    {
        var thresholdUtc = DateTime.UtcNow.AddMonths(-6);
        var actions = new List<ActionItem>();

        foreach (var file in files)
        {
            DateTime modified;
            try
            {
                modified = File.GetLastWriteTimeUtc(file.Path);
            }
            catch
            {
                continue;
            }

            if (modified >= thresholdUtc)
            {
                continue;
            }

            var destinationDirectory = Path.Combine(targetFolder, "Archive", modified.ToString("yyyy-MM"));
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(file.Path));
            if (string.Equals(file.Path, destinationPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            actions.Add(new ActionItem(ActionType.Move, file.Path, destinationPath, file.Size, file.IsSystemFolder, file.IsExecutable));
        }

        return actions;
    }

    private static List<ActionItem> BuildRenameActions(IReadOnlyList<FileMetadata> files, string? pattern)
    {
        var safePattern = string.IsNullOrWhiteSpace(pattern) ? "Renamed" : SanitizeFileName(pattern);
        var actions = new List<ActionItem>();
        var counter = 1;

        foreach (var file in files)
        {
            var directory = Path.GetDirectoryName(file.Path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var extension = string.IsNullOrWhiteSpace(file.Extension) ? string.Empty : file.Extension;
            var newName = $"{safePattern}_{counter:000}{extension}";
            var destinationPath = Path.Combine(directory, newName);
            counter++;

            if (string.Equals(file.Path, destinationPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            actions.Add(new ActionItem(ActionType.Rename, file.Path, destinationPath, file.Size, file.IsSystemFolder, file.IsExecutable));
        }

        return actions;
    }

    private static string SanitizeFileName(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(input.Where(character => !invalid.Contains(character)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "Renamed" : sanitized.Replace(' ', '_');
    }

    private static RiskLevel InferRisk(IReadOnlyList<ActionItem> actions, IntentCategory intent)
    {
        if (actions.Any(action => action.IsSystemPath || action.IsExecutable))
        {
            return RiskLevel.High;
        }

        if (intent == IntentCategory.CleanTemp || actions.Any(action => action.Type == ActionType.Delete))
        {
            return RiskLevel.Medium;
        }

        if (intent == IntentCategory.BatchRename)
        {
            return RiskLevel.Medium;
        }

        return RiskLevel.Low;
    }

    private static PlanDTO BuildHelpPlan(string userInput, IntentResult intent)
    {
        var helpText = "Supported offline intents: organize files, clean temp, batch rename, archive old files, system info, help. " +
                       "Examples: 'organize my desktop screenshots', 'clean temp', 'archive old files in documents', 'organize it again'.";

        return new PlanDTO(Guid.NewGuid(), helpText, RiskLevel.Low, new List<ActionItemDTO>(), DateTime.UtcNow)
        {
            SourceCommand = userInput
        };
    }
}
