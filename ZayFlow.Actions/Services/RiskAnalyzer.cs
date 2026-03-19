using ZayFlow.Core.Abstractions;
using ZayFlow.Core.Domain.Enums;
using ZayFlow.Core.Domain.Models;

namespace ZayFlow.Actions.Services;

public sealed class RiskAnalyzer : IRiskAnalyzer
{
    private const long TwoGbBytes = 2L * 1024 * 1024 * 1024;
    private const long FiveHundredMbBytes = 500L * 1024 * 1024;

    public RiskLevel Analyze(IReadOnlyList<ActionItem> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        if (actions.Count == 0)
        {
            return RiskLevel.Low;
        }

        var totalSize = actions.Sum(x => x.FileSizeBytes);
        var involvesSystemPath = actions.Any(x => x.IsSystemPath || IsProtectedSystemPath(x.SourcePath) || IsProtectedSystemPath(x.DestinationPath));
        var hasHighRiskExtension = actions.Any(x => x.IsExecutable || HasHighRiskExtension(x.SourcePath) || HasHighRiskExtension(x.DestinationPath));

        if (involvesSystemPath || hasHighRiskExtension || actions.Count > 500 || totalSize > TwoGbBytes)
        {
            return RiskLevel.High;
        }

        if (actions.Count >= 100 || totalSize >= FiveHundredMbBytes)
        {
            return RiskLevel.Medium;
        }

        return RiskLevel.Low;
    }

    private static bool HasHighRiskExtension(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".sys", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProtectedSystemPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = NormalizePath(path);
        var windows = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        var programFiles = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        var programFilesX86 = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

        return normalized.StartsWith(windows, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path.Trim());
    }
}
