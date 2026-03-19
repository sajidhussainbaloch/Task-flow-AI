using ZayFlow.Backend.DTOs;

namespace ZayFlow.Backend.Contracts;

public interface IPlanner
{
    Task<PlanDTO> GeneratePlanAsync(string userInput, CancellationToken cancellationToken = default);
}
