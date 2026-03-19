using ZayFlow.Core.Domain.Enums;

namespace ZayFlow.Core.Domain.Models;

public sealed record PreviewItem
{
    public ActionType ActionType { get; }
    public string SourcePath { get; }
    public string DestinationPath { get; }
    public bool IsRisky { get; }
    public string RiskReason { get; }

    public PreviewItem(
        ActionType actionType,
        string sourcePath,
        string destinationPath,
        bool isRisky,
        string riskReason)
    {
        ActionType = actionType;
        SourcePath = sourcePath ?? throw new ArgumentNullException(nameof(sourcePath));
        DestinationPath = destinationPath ?? string.Empty;
        IsRisky = isRisky;
        RiskReason = riskReason ?? string.Empty;
    }
}
