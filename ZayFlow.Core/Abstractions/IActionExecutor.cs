using ZayFlow.Core.Domain.Models;

namespace ZayFlow.Core.Abstractions;

public interface IActionExecutor
{
    Task<ExecutionResult> ExecuteAsync(Plan plan, CancellationToken cancellationToken = default);
}
