using ZayFlow.Core.Domain.Models;

namespace ZayFlow.Core.Abstractions;

public interface IPlanner
{
    Task<Plan> GeneratePlanAsync(string userInput, CancellationToken cancellationToken = default);
}
