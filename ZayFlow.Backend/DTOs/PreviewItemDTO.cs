namespace ZayFlow.Backend.DTOs;

public sealed record PreviewItemDTO(
    string ActionType,
    string SourcePath,
    string DestinationPath,
    bool IsRisky,
    string RiskReason);
