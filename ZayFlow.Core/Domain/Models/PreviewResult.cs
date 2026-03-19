using ZayFlow.Core.Domain.Enums;

namespace ZayFlow.Core.Domain.Models;

public sealed record PreviewResult
{
    public Guid PlanId { get; }
    public string Description { get; }
    public RiskLevel RiskLevel { get; }
    public IReadOnlyList<PreviewItem> Items { get; }

    public PreviewResult(Guid planId, string description, RiskLevel riskLevel, IReadOnlyList<PreviewItem> items)
    {
        PlanId = planId;
        Description = description ?? throw new ArgumentNullException(nameof(description));
        RiskLevel = riskLevel;
        Items = items ?? throw new ArgumentNullException(nameof(items));
    }
}
