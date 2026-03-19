namespace ZayFlow.Backend.DTOs;

public sealed record PreviewDTO(
    Guid PlanId,
    string Description,
    string RiskLevel,
    IReadOnlyList<PreviewItemDTO> Items);
