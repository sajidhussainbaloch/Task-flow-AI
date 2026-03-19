using ZayFlow.Core.Domain.Enums;
using ZayFlow.Core.Domain.Models;

namespace ZayFlow.Core.Abstractions;

public interface IRiskAnalyzer
{
    RiskLevel Analyze(IReadOnlyList<ActionItem> actions);
}
