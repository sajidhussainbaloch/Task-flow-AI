using ZayFlow.Core.Domain.Enums;

namespace ZayFlow.Core.Domain.Models;

public sealed record FailedExecutionItem
{
    public ActionType ActionType { get; }
    public string SourcePath { get; }
    public string DestinationPath { get; }
    public string Error { get; }

    public FailedExecutionItem(ActionType actionType, string sourcePath, string destinationPath, string error)
    {
        ActionType = actionType;
        SourcePath = sourcePath ?? throw new ArgumentNullException(nameof(sourcePath));
        DestinationPath = destinationPath ?? string.Empty;
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }
}
