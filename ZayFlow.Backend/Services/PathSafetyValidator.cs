using ZayFlow.Backend.Contracts;

namespace ZayFlow.Backend.Services;

public sealed class PathSafetyValidator : IPathSafetyValidator
{
    private static readonly string[] BlockedRoots =
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
    };

    public bool IsPathAllowed(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = Normalize(path);
        if (IsDriveRoot(normalized))
        {
            return false;
        }

        return !BlockedRoots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(Normalize)
            .Any(blocked => normalized.StartsWith(blocked, StringComparison.OrdinalIgnoreCase));
    }

    public void EnsurePathAllowed(string path)
    {
        if (!IsPathAllowed(path))
        {
            throw new InvalidOperationException($"Blocked unsafe path: {path}");
        }
    }

    private static bool IsDriveRoot(string path)
    {
        var root = Path.GetPathRoot(path);
        return string.Equals(path, root, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
    {
        var trimmed = path.Trim();

        if (trimmed.Contains(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || trimmed.Contains("../", StringComparison.Ordinal)
            || trimmed.Contains("..\\", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Path traversal is not allowed.");
        }

        return Path.GetFullPath(trimmed);
    }
}
