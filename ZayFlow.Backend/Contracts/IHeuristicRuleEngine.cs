using ZayFlow.Backend.Models;
using ZayFlow.Core.Domain.Models;

namespace ZayFlow.Backend.Contracts;

public interface IHeuristicRuleEngine
{
    HeuristicInsight Analyze(string targetFolder, IReadOnlyList<FileMetadata> files, IntentResult intent);
}
