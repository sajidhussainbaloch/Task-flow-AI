using ZayFlow.Core.Domain.Models;

namespace ZayFlow.Core.Abstractions;

public interface IAutomationService
{
    Task<Plan> GeneratePlanAsync(string input, CancellationToken cancellationToken = default);
    Task<PreviewResult> PreviewAsync(Plan plan, CancellationToken cancellationToken = default);
    Task<ExecutionResult> ExecuteAsync(Plan plan, CancellationToken cancellationToken = default);
    Task<ExecutionResult> UndoLastAsync(CancellationToken cancellationToken = default);
}
