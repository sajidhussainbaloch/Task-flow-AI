using ZayFlow.Core.Domain.Enums;

namespace ZayFlow.Core.Domain.Models;

public sealed record ExecutedAction
{
    public ActionType OriginalActionType { get; }
    public string OriginalSourcePath { get; }
    public string OriginalDestinationPath { get; }
    public string CurrentPath { get; }

    public ExecutedAction(
        ActionType originalActionType,
        string originalSourcePath,
        string originalDestinationPath,
        string currentPath)
    {
        OriginalActionType = originalActionType;
        OriginalSourcePath = originalSourcePath ?? throw new ArgumentNullException(nameof(originalSourcePath));
        OriginalDestinationPath = originalDestinationPath ?? string.Empty;
        CurrentPath = currentPath ?? throw new ArgumentNullException(nameof(currentPath));
    }
}
