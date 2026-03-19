using Microsoft.Extensions.Logging;
using ZayFlow.Core.Abstractions;
using ZayFlow.Core.Domain.Enums;
using ZayFlow.Core.Domain.Models;

namespace ZayFlow.Actions.Services;

public sealed class UndoManager : IUndoManager
{
    private readonly ILogger<UndoManager> _logger;
    private readonly Stack<UndoBatch> _history;

    public UndoManager(ILogger<UndoManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _history = new Stack<UndoBatch>();
    }

    public Task StoreBatchAsync(Plan plan, IReadOnlyList<ExecutedAction> executedActions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(executedActions);

        cancellationToken.ThrowIfCancellationRequested();

        if (executedActions.Count == 0)
        {
            return Task.CompletedTask;
        }

        _history.Push(new UndoBatch(plan.Id, DateTime.UtcNow, executedActions.ToList()));

        _logger.LogInformation(
            "Execution batch stored for undo. PlanId: {PlanId}, ActionCount: {ActionCount}, HistoryDepth: {Depth}",
            plan.Id,
            executedActions.Count,
            _history.Count);

        return Task.CompletedTask;
    }

    public async Task<ExecutionResult> UndoLastAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_history.Count == 0)
        {
            _logger.LogInformation("Undo requested but no batch history exists.");
            return ExecutionResult.Empty;
        }

        var batch = _history.Pop();
        var failedItems = new List<FailedExecutionItem>();
        var successfulUndos = 0;
        var executedUndoActions = new List<ExecutedAction>();

        foreach (var action in batch.ExecutedActions.AsEnumerable().Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                switch (action.OriginalActionType)
                {
                    case ActionType.Move:
                    case ActionType.Rename:
                        await UndoMoveOrRenameAsync(action, cancellationToken).ConfigureAwait(false);
                        successfulUndos++;
                        executedUndoActions.Add(new ExecutedAction(
                            action.OriginalActionType,
                            action.OriginalDestinationPath,
                            action.OriginalSourcePath,
                            action.OriginalSourcePath));
                        break;

                    case ActionType.Delete:
                        throw new InvalidOperationException("Delete operations cannot be undone without persistent backup.");

                    default:
                        throw new InvalidOperationException($"Unsupported action type for undo: {action.OriginalActionType}");
                }

                _logger.LogInformation(
                    "Undo successful. Type: {ActionType}, From: {CurrentPath}, To: {OriginalPath}",
                    action.OriginalActionType,
                    action.CurrentPath,
                    action.OriginalSourcePath);
            }
            catch (Exception ex)
            {
                failedItems.Add(new FailedExecutionItem(
                    action.OriginalActionType,
                    action.CurrentPath,
                    action.OriginalSourcePath,
                    ex.Message));

                _logger.LogWarning(
                    ex,
                    "Undo failed. Type: {ActionType}, Current: {CurrentPath}, Original: {OriginalPath}",
                    action.OriginalActionType,
                    action.CurrentPath,
                    action.OriginalSourcePath);
            }
        }

        return new ExecutionResult(
            successfulUndos,
            failedItems.Count,
            failedItems,
            executedUndoActions);
    }

    private static async Task UndoMoveOrRenameAsync(ExecutedAction action, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(action.CurrentPath) || !File.Exists(action.CurrentPath))
        {
            throw new FileNotFoundException("Current file path not found for undo.", action.CurrentPath);
        }

        var destinationDirectory = Path.GetDirectoryName(action.OriginalSourcePath);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new InvalidOperationException("Original source directory is invalid.");
        }

        Directory.CreateDirectory(destinationDirectory);

        await Task.Run(() => File.Move(action.CurrentPath, action.OriginalSourcePath), cancellationToken).ConfigureAwait(false);
    }

    private sealed record UndoBatch(Guid PlanId, DateTime StoredAt, IReadOnlyList<ExecutedAction> ExecutedActions);
}
