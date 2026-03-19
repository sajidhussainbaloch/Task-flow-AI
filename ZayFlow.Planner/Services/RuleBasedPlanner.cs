using Microsoft.Extensions.Logging;
using ZayFlow.Core.Abstractions;
using ZayFlow.Core.Domain.Enums;
using ZayFlow.Core.Domain.Models;

namespace ZayFlow.Planner.Services;

public sealed class RuleBasedPlanner : IPlanner
{
    private readonly IFileScanner _fileScanner;
    private readonly ILogger<RuleBasedPlanner> _logger;

    public RuleBasedPlanner(IFileScanner fileScanner, ILogger<RuleBasedPlanner> logger)
    {
        _fileScanner = fileScanner ?? throw new ArgumentNullException(nameof(fileScanner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Plan> GeneratePlanAsync(string userInput, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userInput))
        {
            throw new ArgumentException("Planner input cannot be empty.", nameof(userInput));
        }

        var normalizedInput = userInput.Trim().ToLowerInvariant();

        var (description, targetDirectory, actionFactory) = normalizedInput switch
        {
            "sort downloads" => (
                "Sort files in Downloads by extension",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads",
                new Func<FileMetadata, ActionItem>(metadata => CreateMoveAction(metadata, "Sorted"))),

            "organize desktop" => (
                "Organize Desktop files by extension",
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                new Func<FileMetadata, ActionItem>(metadata => CreateMoveAction(metadata, "Organized"))),

            "clean temp files" => (
                "Delete files in Temp directory",
                Path.GetTempPath(),
                new Func<FileMetadata, ActionItem>(CreateDeleteAction)),

            _ => throw new InvalidOperationException(
                "Unsupported command. Supported commands: sort downloads, organize desktop, clean temp files")
        };

        var target = Path.GetFullPath(targetDirectory);
        var scannedFiles = await _fileScanner.ScanDirectoryAsync(target, cancellationToken).ConfigureAwait(false);

        var actions = scannedFiles
            .Select(actionFactory)
            .Where(action => !string.IsNullOrWhiteSpace(action.SourcePath))
            .ToList();

        var plan = new Plan(
            Guid.NewGuid(),
            description,
            actions,
            RiskLevel.Low,
            DateTime.UtcNow);

        _logger.LogInformation(
            "RuleBased plan generated. Input: {Input}, Target: {Target}, ActionCount: {ActionCount}",
            userInput,
            target,
            actions.Count);

        return plan;
    }

    private static ActionItem CreateMoveAction(FileMetadata metadata, string rootFolderName)
    {
        var extension = string.IsNullOrWhiteSpace(metadata.Extension)
            ? "NoExtension"
            : metadata.Extension.TrimStart('.').ToUpperInvariant();

        var sourceDirectory = Path.GetDirectoryName(metadata.Path) ?? string.Empty;
        var destinationDirectory = Path.Combine(sourceDirectory, rootFolderName, extension);
        var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(metadata.Path));

        return new ActionItem(
            ActionType.Move,
            metadata.Path,
            destinationPath,
            metadata.Size,
            metadata.IsSystemFolder,
            metadata.IsExecutable);
    }

    private static ActionItem CreateDeleteAction(FileMetadata metadata)
    {
        return new ActionItem(
            ActionType.Delete,
            metadata.Path,
            string.Empty,
            metadata.Size,
            metadata.IsSystemFolder,
            metadata.IsExecutable);
    }
}
