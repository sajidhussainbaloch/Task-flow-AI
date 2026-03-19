namespace ZayFlow.Backend.DTOs;

public sealed record FailedExecutionItemDTO(
    string ActionType,
    string SourcePath,
    string DestinationPath,
    string Error);
