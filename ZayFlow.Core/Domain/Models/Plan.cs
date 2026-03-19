using ZayFlow.Core.Domain.Enums;

namespace ZayFlow.Core.Domain.Models;

public sealed record Plan
{
    public Guid Id { get; }
    public string Description { get; }
    public IReadOnlyList<ActionItem> Actions { get; }
    public RiskLevel RiskLevel { get; }
    public DateTime CreatedAt { get; }

    public Plan(
        Guid id,
        string description,
        IReadOnlyList<ActionItem> actions,
        RiskLevel riskLevel,
        DateTime createdAt)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        Description = string.IsNullOrWhiteSpace(description)
            ? throw new ArgumentException("Plan description is required.", nameof(description))
            : description.Trim();
        Actions = actions ?? throw new ArgumentNullException(nameof(actions));
        RiskLevel = riskLevel;
        CreatedAt = createdAt;
    }
}
