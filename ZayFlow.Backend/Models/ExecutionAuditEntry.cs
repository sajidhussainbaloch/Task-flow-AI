using ZayFlow.Backend.DTOs;

namespace ZayFlow.Backend.Models;

public sealed class ExecutionAuditEntry
{
    public DateTime OccurredAtUtc { get; set; }
    public string Operation { get; set; } = string.Empty;
    public PlanDTO? Plan { get; set; }
    public ExecutionResultDTO? Result { get; set; }
}
