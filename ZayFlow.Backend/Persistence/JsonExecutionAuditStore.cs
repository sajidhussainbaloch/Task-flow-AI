using System.Text.Json;
using ZayFlow.Backend.Contracts;
using ZayFlow.Backend.Models;

namespace ZayFlow.Backend.Persistence;

public sealed class JsonExecutionAuditStore : IExecutionAuditStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task AppendAsync(ExecutionAuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var line = JsonSerializer.Serialize(entry, Options);
        await File.AppendAllTextAsync(StoragePathProvider.AuditLogPath, line + Environment.NewLine, cancellationToken).ConfigureAwait(false);
    }
}
