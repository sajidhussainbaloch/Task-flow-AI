using System.Text.RegularExpressions;
using ZayFlow.Backend.Contracts;
using ZayFlow.Backend.Models;

namespace ZayFlow.Backend.Services;

public sealed class IntentDetector : IIntentDetector
{
    private static readonly Regex PathPattern = new(@"([a-zA-Z]:[^\r\n""\*\?<>\|]+)", RegexOptions.Compiled);

    public IntentResult DetectIntent(string userInput)
    {
        if (string.IsNullOrWhiteSpace(userInput))
        {
            return new IntentResult
            {
                OriginalInput = string.Empty,
                NormalizedInput = string.Empty,
                Intent = IntentCategory.Unknown,
                Confidence = 0,
                Reason = "Input is empty"
            };
        }

        var normalized = userInput.Trim().ToLowerInvariant();
        var detectedPath = TryExtractPath(userInput);
        var folderHint = detectedPath ?? TryResolveFolderHint(normalized);
        var fileTypeHint = TryExtractFileTypeHint(normalized);
        var renamePattern = TryExtractRenamePattern(normalized);
        var useLastContext = normalized.Contains("again", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("it", StringComparison.OrdinalIgnoreCase);

        if (ContainsAny(normalized, "help", "what can you do", "commands", "examples"))
        {
            return Build(IntentCategory.Help, userInput, normalized, folderHint, fileTypeHint, renamePattern, useLastContext, 0.98, "Help keywords matched");
        }

        if (ContainsAny(normalized, "system info", "disk usage", "storage", "space"))
        {
            return Build(IntentCategory.SystemInfo, userInput, normalized, folderHint, fileTypeHint, renamePattern, useLastContext, 0.93, "System information keywords matched");
        }

        if (ContainsAny(normalized, "open ", "launch ", "start app", "run app"))
        {
            return Build(IntentCategory.OpenApplication, userInput, normalized, folderHint, fileTypeHint, renamePattern, useLastContext, 0.85, "Open application keywords matched");
        }

        if (ContainsAny(normalized, "rename", "batch rename", "rename files", "rename all"))
        {
            return Build(IntentCategory.BatchRename, userInput, normalized, folderHint, fileTypeHint, renamePattern, useLastContext, 0.9, "Rename keywords matched");
        }

        if (ContainsAny(normalized, "archive", "old files", "archive old", "move old"))
        {
            return Build(IntentCategory.ArchiveOldFiles, userInput, normalized, folderHint, fileTypeHint, renamePattern, useLastContext, 0.92, "Archive keywords matched");
        }

        if (ContainsAny(normalized, "temp", "temporary", "cache", "junk"))
        {
            return Build(IntentCategory.CleanTemp, userInput, normalized, folderHint, fileTypeHint, renamePattern, useLastContext, 0.91, "Temporary cleanup keywords matched");
        }

        if (ContainsAny(normalized, "organize", "sort", "clean", "arrange", "tidy"))
        {
            return Build(IntentCategory.OrganizeFiles, userInput, normalized, folderHint, fileTypeHint, renamePattern, useLastContext, 0.89, "Organization keywords matched");
        }

        return Build(IntentCategory.Unknown, userInput, normalized, folderHint, fileTypeHint, renamePattern, useLastContext, 0.25, "No intent pattern matched");
    }

    private static IntentResult Build(
        IntentCategory intent,
        string original,
        string normalized,
        string? folder,
        string? fileType,
        string? renamePattern,
        bool useLastContext,
        double confidence,
        string reason)
    {
        return new IntentResult
        {
            Intent = intent,
            OriginalInput = original,
            NormalizedInput = normalized,
            TargetFolderPath = folder,
            FileTypeHint = fileType,
            RenamePattern = renamePattern,
            UseLastContext = useLastContext,
            Confidence = confidence,
            Reason = reason
        };
    }

    private static string? TryExtractPath(string input)
    {
        var match = PathPattern.Match(input);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string? TryResolveFolderHint(string normalized)
    {
        if (normalized.Contains("downloads", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        if (normalized.Contains("desktop", StringComparison.OrdinalIgnoreCase))
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        if (normalized.Contains("documents", StringComparison.OrdinalIgnoreCase))
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        if (normalized.Contains("pictures", StringComparison.OrdinalIgnoreCase) || normalized.Contains("photos", StringComparison.OrdinalIgnoreCase))
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        }

        if (normalized.Contains("music", StringComparison.OrdinalIgnoreCase))
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        }

        if (normalized.Contains("videos", StringComparison.OrdinalIgnoreCase))
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        }

        if (normalized.Contains("temp", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetTempPath();
        }

        return null;
    }

    private static string? TryExtractFileTypeHint(string normalized)
    {
        if (ContainsAny(normalized, "screenshot", "image", "photo", "picture", "png", "jpg", "jpeg", "gif", "bmp"))
        {
            return "images";
        }

        if (ContainsAny(normalized, "document", "doc", "pdf", "ppt", "xls", "txt"))
        {
            return "documents";
        }

        if (ContainsAny(normalized, "video", "mp4", "avi", "mkv", "mov"))
        {
            return "videos";
        }

        if (ContainsAny(normalized, "music", "audio", "mp3", "wav", "flac"))
        {
            return "audio";
        }

        return null;
    }

    private static string? TryExtractRenamePattern(string normalized)
    {
        var marker = "rename";
        var index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var tail = normalized[(index + marker.Length)..].Trim();
        if (string.IsNullOrWhiteSpace(tail))
        {
            return null;
        }

        return tail
            .Replace("files", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("to", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static bool ContainsAny(string input, params string[] tokens)
    {
        return tokens.Any(token => input.Contains(token, StringComparison.OrdinalIgnoreCase));
    }
}
