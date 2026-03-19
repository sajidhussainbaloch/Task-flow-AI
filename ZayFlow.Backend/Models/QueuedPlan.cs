using ZayFlow.Backend.DTOs;
using ZayFlow.Core.Domain.Enums;

namespace ZayFlow.Backend.Models;

public sealed class QueuedPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime QueuedAtUtc { get; set; } = DateTime.UtcNow;
    public PlanDTO Plan { get; set; } = new(Guid.Empty, string.Empty, RiskLevel.Low, new List<ActionItemDTO>(), DateTime.UtcNow);
}
