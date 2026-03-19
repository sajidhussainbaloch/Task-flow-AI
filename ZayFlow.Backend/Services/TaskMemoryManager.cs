using ZayFlow.Backend.Contracts;
using ZayFlow.Backend.Models;

namespace ZayFlow.Backend.Services;

public sealed class TaskMemoryManager : ITaskMemoryManager
{
    private const int MaxRecentCommands = 20;
    private readonly ITaskMemoryStore _store;

    public TaskMemoryManager(ITaskMemoryStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<TaskMemoryState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(cancellationToken);
    }

    public async Task<string?> ResolveLastTargetFolderAsync(CancellationToken cancellationToken = default)
    {
        var state = await _store.GetAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(state.LastTargetFolder) ? null : state.LastTargetFolder;
    }

    public async Task RememberPlanningContextAsync(
        string userInput,
        IntentResult intent,
        string? targetFolder,
        string? organizationStyle,
        CancellationToken cancellationToken = default)
    {
        var state = await _store.GetAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(targetFolder))
        {
            state.LastTargetFolder = targetFolder;
            if (state.FrequentFolders.TryGetValue(targetFolder, out var count))
            {
                state.FrequentFolders[targetFolder] = count + 1;
            }
            else
            {
                state.FrequentFolders[targetFolder] = 1;
            }
        }

        if (!string.IsNullOrWhiteSpace(organizationStyle))
        {
            state.PreferredOrganizationStyle = organizationStyle;
        }

        state.LastIntent = intent.Intent;
        await _store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
    }

    public async Task RememberExecutedCommandAsync(string commandText, string? targetFolder, CancellationToken cancellationToken = default)
    {
        var state = await _store.GetAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(commandText))
        {
            state.RecentExecutedCommands.Insert(0, commandText.Trim());
            if (state.RecentExecutedCommands.Count > MaxRecentCommands)
            {
                state.RecentExecutedCommands = state.RecentExecutedCommands.Take(MaxRecentCommands).ToList();
            }
        }

        if (!string.IsNullOrWhiteSpace(targetFolder))
        {
            state.LastTargetFolder = targetFolder;
            if (state.FrequentFolders.TryGetValue(targetFolder, out var count))
            {
                state.FrequentFolders[targetFolder] = count + 1;
            }
            else
            {
                state.FrequentFolders[targetFolder] = 1;
            }
        }

        await _store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
    }
}
