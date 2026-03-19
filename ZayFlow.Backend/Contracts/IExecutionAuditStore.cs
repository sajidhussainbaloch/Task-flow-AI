using ZayFlow.Backend.Models;

namespace ZayFlow.Backend.Contracts;

public interface IExecutionAuditStore
{
    Task AppendAsync(ExecutionAuditEntry entry, CancellationToken cancellationToken = default);
}
