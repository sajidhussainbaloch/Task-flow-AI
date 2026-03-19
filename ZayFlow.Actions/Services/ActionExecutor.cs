using Microsoft.Extensions.Logging;
using ZayFlow.Core.Abstractions;
using ZayFlow.Core.Domain.Enums;
using ZayFlow.Core.Domain.Models;

namespace ZayFlow.Actions.Services;

public sealed class ActionExecutor : IActionExecutor
{
    private readonly ILogger<ActionExecutor> _logger;

    public ActionExecutor(ILogger<ActionExecutor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ExecutionResult> ExecuteAsync(Plan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var failedItems = new List<FailedExecutionItem>();
        var executedActions = new List<ExecutedAction>();
        var successCount = 0;

        foreach (var action in plan.Actions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var normalizedSource = NormalizePath(action.SourcePath);
                var normalizedDestination = string.IsNullOrWhiteSpace(action.DestinationPath)
                    ? string.Empty
                    : NormalizePath(action.DestinationPath);

                ValidateActionPaths(action.Type, normalizedSource, normalizedDestination);

                switch (action.Type)
                {
                    case ActionType.Move:
                    case ActionType.Rename:
                        await MoveOrRenameAsync(action, normalizedSource, normalizedDestination, cancellationToken)
                            .ConfigureAwait(false);

                        executedActions.Add(new ExecutedAction(
                            action.Type,
                            normalizedSource,
                            normalizedDestination,
                            normalizedDestination));
                        break;

                    case ActionType.Delete:
                        await DeleteAsync(normalizedSource, cancellationToken).ConfigureAwait(false);

                        executedActions.Add(new ExecutedAction(
                            action.Type,
                            normalizedSource,
                            string.Empty,
                            string.Empty));
                        break;

                    default:
                        throw new InvalidOperationException($"Unsupported action type: {action.Type}");
                }

                successCount++;

                _logger.LogInformation(
                    "Action executed successfully. Type: {ActionType}, Source: {Source}, Destination: {Destination}",
                    action.Type,
                    normalizedSource,
                    normalizedDestination);
            }
            catch (Exception ex)
            {
                failedItems.Add(new FailedExecutionItem(action.Type, action.SourcePath, action.DestinationPath, ex.Message));
                _logger.LogWarning(
                    ex,
                    "Action failed. Type: {ActionType}, Source: {Source}, Destination: {Destination}",
                    action.Type,
                    action.SourcePath,
                    action.DestinationPath);
            }
        }

        return new ExecutionResult(
            successCount,
            failedItems.Count,
            failedItems,
            executedActions);
    }

    private static async Task MoveOrRenameAsync(
        ActionItem action,
        string normalizedSource,
        string normalizedDestination,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(normalizedSource))
        {
            throw new FileNotFoundException("Source file not found.", normalizedSource);
        }

        if (string.IsNullOrWhiteSpace(normalizedDestination))
        {
            throw new InvalidOperationException("Destination path is required for move/rename actions.");
        }

        var destinationDirectory = Path.GetDirectoryName(normalizedDestination);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new InvalidOperationException("Invalid destination directory.");
        }

        Directory.CreateDirectory(destinationDirectory);

        await Task.Run(() => File.Move(normalizedSource, normalizedDestination), cancellationToken).ConfigureAwait(false);
    }

    private static async Task DeleteAsync(string normalizedSource, CancellationToken cancellationToken)
    {
        if (!File.Exists(normalizedSource))
        {
            throw new FileNotFoundException("Source file not found for deletion.", normalizedSource);
        }

        await Task.Run(() => File.Delete(normalizedSource), cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateActionPaths(ActionType actionType, string sourcePath, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new InvalidOperationException("Source path is required.");
        }

        if (IsDriveRoot(sourcePath))
        {
            throw new InvalidOperationException("Root paths are not allowed.");
        }

        if (IsProtectedSystemPath(sourcePath))
        {
            throw new InvalidOperationException("Operations in protected system paths are blocked.");
        }

        if ((actionType == ActionType.Move || actionType == ActionType.Rename) && string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new InvalidOperationException("Destination path is required.");
        }

        if (!string.IsNullOrWhiteSpace(destinationPath))
        {
            if (IsDriveRoot(destinationPath))
            {
                throw new InvalidOperationException("Destination root paths are not allowed.");
            }

            if (IsProtectedSystemPath(destinationPath))
            {
                throw new InvalidOperationException("Destination in protected system paths is blocked.");
            }

            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrWhiteSpace(destinationDirectory))
            {
                throw new InvalidOperationException("Destination directory is invalid.");
            }

            var fullDirectoryPath = NormalizePath(destinationDirectory);
            if (IsProtectedSystemPath(fullDirectoryPath))
            {
                throw new InvalidOperationException("Destination directory resolves to protected path.");
            }
        }
    }

    private static bool IsDriveRoot(string path)
    {
        var normalized = NormalizePath(path);
        var root = Path.GetPathRoot(normalized);
        return string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProtectedSystemPath(string path)
    {
        var normalized = NormalizePath(path);
        var windows = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        var programFiles = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        var programFilesX86 = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

        return normalized.StartsWith(windows, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        var trimmed = path.Trim();
        var fullPath = Path.GetFullPath(trimmed);

        if (trimmed.Contains(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || trimmed.Contains("../", StringComparison.Ordinal)
            || trimmed.Contains("..\\", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Path traversal patterns are not allowed.");
        }

        return fullPath;
    }
}
