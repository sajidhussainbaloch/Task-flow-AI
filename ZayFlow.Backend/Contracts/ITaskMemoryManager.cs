using ZayFlow.Backend.Models;

namespace ZayFlow.Backend.Contracts;

public interface ITaskMemoryManager
{
    Task<TaskMemoryState> GetStateAsync(CancellationToken cancellationToken = default);
    Task<string?> ResolveLastTargetFolderAsync(CancellationToken cancellationToken = default);
    Task RememberPlanningContextAsync(string userInput, IntentResult intent, string? targetFolder, string? organizationStyle, CancellationToken cancellationToken = default);
    Task RememberExecutedCommandAsync(string commandText, string? targetFolder, CancellationToken cancellationToken = default);
}
