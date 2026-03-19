using ZayFlow.Backend.DTOs;
using ZayFlow.Backend.Models;

namespace ZayFlow.Backend.Contracts;

public interface ISuggestionService
{
    Task<IReadOnlyList<SuggestionDTO>> GetSuggestionsAsync(string? targetFolder = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SuggestionDTO>> GetSuggestionsForIntentAsync(IntentResult intent, CancellationToken cancellationToken = default);
}
