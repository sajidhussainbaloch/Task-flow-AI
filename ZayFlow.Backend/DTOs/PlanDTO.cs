using ZayFlow.Core.Domain.Enums;

namespace ZayFlow.Backend.DTOs;

public sealed class PlanDTO
{
    public Guid Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<ActionItemDTO> Actions { get; set; } = new();
    public RiskLevel RiskLevel { get; set; }
    public DateTime CreatedAt { get; set; }
    public string SourceCommand { get; set; } = string.Empty;

    public PlanDTO()
    {
    }

    public PlanDTO(Guid id, string description, RiskLevel riskLevel, List<ActionItemDTO> actions, DateTime createdAt, string sourceCommand = "")
    {
        Id = id;
        Description = description;
        RiskLevel = riskLevel;
        Actions = actions;
        CreatedAt = createdAt;
        SourceCommand = sourceCommand;
    }
}
