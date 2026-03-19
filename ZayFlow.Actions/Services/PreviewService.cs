using ZayFlow.Core.Abstractions;
using ZayFlow.Core.Domain.Models;

namespace ZayFlow.Actions.Services;

public sealed class PreviewService : IPreviewService
{
    public PreviewResult BuildPreview(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var items = plan.Actions
            .Select(action =>
            {
                var riskReason = BuildRiskReason(action);
                return new PreviewItem(
                    action.Type,
                    action.SourcePath,
                    action.DestinationPath,
                    !string.IsNullOrEmpty(riskReason),
                    riskReason);
            })
            .ToList();

        return new PreviewResult(plan.Id, plan.Description, plan.RiskLevel, items);
    }

    private static string BuildRiskReason(ActionItem action)
    {
        if (action.IsSystemPath)
        {
            return "System path involved";
        }

        if (action.IsExecutable)
        {
            return "Executable/system file type";
        }

        if (action.Type == Core.Domain.Enums.ActionType.Delete)
        {
            return "Destructive delete operation";
        }

        return string.Empty;
    }
}
