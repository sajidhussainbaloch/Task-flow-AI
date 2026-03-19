using ZayFlow.Backend.DTOs;
using ZayFlow.Core.Domain.Enums;
using ZayFlow.Core.Domain.Models;

namespace ZayFlow.Backend.Mapping;

internal static class BackendMapper
{
    public static PlanDTO ToDto(Plan plan)
    {
        return new PlanDTO(
            plan.Id,
            plan.Description,
            plan.RiskLevel,
            plan.Actions.Select(ToDto).ToList(),
            plan.CreatedAt,
            string.Empty);
    }

    public static Plan ToDomain(PlanDTO dto)
    {
        var actions = dto.Actions.Select(ToDomain).ToList();

        return new Plan(
            dto.Id,
            dto.Description,
            actions,
            dto.RiskLevel,
            dto.CreatedAt);
    }

    public static PreviewDTO ToDto(PreviewResult preview)
    {
        return new PreviewDTO(
            preview.PlanId,
            preview.Description,
            preview.RiskLevel.ToString(),
            preview.Items.Select(item => new PreviewItemDTO(
                item.ActionType.ToString(),
                item.SourcePath,
                item.DestinationPath,
                item.IsRisky,
                item.RiskReason)).ToList());
    }

    public static ExecutionResultDTO ToDto(ExecutionResult result, bool wasQueued = false, string? message = null)
    {
        return new ExecutionResultDTO(
            result.SuccessCount,
            result.FailureCount,
            result.FailedItems.Select(f => new FailedExecutionItemDTO(
                f.ActionType.ToString(),
                f.SourcePath,
                f.DestinationPath,
                f.Error)).ToList(),
            wasQueued,
            message ?? string.Empty);
    }

    private static ActionItemDTO ToDto(ActionItem item)
    {
        var fileName = Path.GetFileName(item.SourcePath);
        return new ActionItemDTO(
            item.Type.ToString(),
            $"{item.Type} {fileName}",
            item.SourcePath,
            item.DestinationPath,
            item.FileSizeBytes,
            item.IsSystemPath,
            item.IsExecutable);
    }

    private static ActionItem ToDomain(ActionItemDTO item)
    {
        return new ActionItem(
            ParseActionType(item.Type),
            item.SourcePath,
            item.DestinationPath,
            item.FileSizeBytes,
            item.IsSystemPath,
            item.IsExecutable);
    }

    private static ActionType ParseActionType(string type)
    {
        return Enum.TryParse<ActionType>(type, ignoreCase: true, out var value) ? value : ActionType.Move;
    }
}
