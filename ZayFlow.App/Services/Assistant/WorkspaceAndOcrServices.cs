using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZayFlow.App.Services.Assistant;

public sealed class WorkspaceContextService : IWorkspaceContextService
{
    private static readonly string[] IgnoreSegments = [".git", "bin", "obj", ".vs", "publish", "installer-output"];
    private static readonly string[] KnownExtensions =
    [
        ".cs", ".xaml", ".json", ".xml", ".md", ".txt", ".py", ".js", ".ts", ".css", ".html", ".ps1", ".yml", ".yaml"
    ];

    public WorkspaceContextSnapshot Capture(string workspaceRoot, string userMessage, string lastCreatedFilePath)
    {
        var snapshot = new WorkspaceContextSnapshot { RootPath = workspaceRoot ?? string.Empty };
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            return snapshot;
        }

        var files = Directory.EnumerateFiles(workspaceRoot, "*", SearchOption.AllDirectories)
            .Where(path => !IgnoreSegments.Any(segment => path.Contains($@"\{segment}\", StringComparison.OrdinalIgnoreCase)))
            .Where(path => KnownExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Take(180)
            .Select(path => Path.GetRelativePath(workspaceRoot, path))
            .OrderBy(path => path.Length)
            .ToList();

        snapshot.RelativeFiles.AddRange(files);

        var fileHints = ExtractFileHints(userMessage);
        if (!string.IsNullOrWhiteSpace(lastCreatedFilePath) && lastCreatedFilePath.StartsWith(workspaceRoot, StringComparison.OrdinalIgnoreCase))
        {
            fileHints.Add(Path.GetRelativePath(workspaceRoot, lastCreatedFilePath));
        }

        foreach (var hint in fileHints.Distinct(StringComparer.OrdinalIgnoreCase).Take(4))
        {
            var match = files.FirstOrDefault(file => file.EndsWith(hint, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).Equals(Path.GetFileName(hint), StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                continue;
            }

            var absolutePath = Path.Combine(workspaceRoot, match);
            try
            {
                snapshot.FileContents[match] = File.ReadAllText(absolutePath);
            }
            catch
            {
                // Ignore unreadable files in prompt context.
            }
        }

        return snapshot;
    }

    private static List<string> ExtractFileHints(string userMessage)
    {
        var hints = new List<string>();
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return hints;
        }

        foreach (Match match in Regex.Matches(userMessage, @"([\w\-.]+\.(cs|xaml|json|xml|md|txt|py|js|ts|css|html|ps1|yml|yaml))", RegexOptions.IgnoreCase))
        {
            hints.Add(match.Groups[1].Value);
        }

        return hints;
    }
}

public sealed class CodeContextBuilder : ICodeContextBuilder
{
    public string BuildPromptContext(AssistantTurnRequest request, WorkspaceContextSnapshot snapshot)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(snapshot.RootPath))
        {
            sb.AppendLine($"WorkspaceRoot: {snapshot.RootPath}");
        }

        if (snapshot.RelativeFiles.Count > 0)
        {
            sb.AppendLine("WorkspaceFiles:");
            foreach (var file in snapshot.RelativeFiles.Take(60))
            {
                sb.AppendLine($"- {file}");
            }
        }

        if (snapshot.FileContents.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("ReferencedFiles:");
            foreach (var kvp in snapshot.FileContents)
            {
                var content = kvp.Value.Length > 5000 ? kvp.Value[..5000] : kvp.Value;
                sb.AppendLine($"--- FILE: {kvp.Key} ---");
                sb.AppendLine(content);
                sb.AppendLine($"--- END FILE: {kvp.Key} ---");
            }
        }

        if (request.Attachments.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("AttachmentOcr:");
            foreach (var attachment in request.Attachments.Where(a => !string.IsNullOrWhiteSpace(a.OcrText)).Take(3))
            {
                sb.AppendLine($"- {attachment.Title}: {attachment.OcrText}");
            }
        }

        return sb.ToString().Trim();
    }
}

public sealed class CodeDiffService : ICodeDiffService
{
    public string BuildUnifiedDiff(string originalContent, string updatedContent, string filePath)
    {
        var originalLines = Normalize(originalContent);
        var updatedLines = Normalize(updatedContent);

        var prefix = 0;
        while (prefix < originalLines.Length && prefix < updatedLines.Length && originalLines[prefix] == updatedLines[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix + prefix < originalLines.Length
            && suffix + prefix < updatedLines.Length
            && originalLines[originalLines.Length - 1 - suffix] == updatedLines[updatedLines.Length - 1 - suffix])
        {
            suffix++;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"--- {filePath}");
        sb.AppendLine($"+++ {filePath}");

        foreach (var line in originalLines.Take(prefix))
        {
            sb.AppendLine($" {line}");
        }

        foreach (var line in originalLines.Skip(prefix).Take(Math.Max(0, originalLines.Length - prefix - suffix)))
        {
            sb.AppendLine($"-{line}");
        }

        foreach (var line in updatedLines.Skip(prefix).Take(Math.Max(0, updatedLines.Length - prefix - suffix)))
        {
            sb.AppendLine($"+{line}");
        }

        foreach (var line in originalLines.Skip(Math.Max(prefix, originalLines.Length - suffix)))
        {
            sb.AppendLine($" {line}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string[] Normalize(string content)
        => (content ?? string.Empty).Replace("\r\n", "\n").Split('\n');
}

public sealed class CodeApplyService : ICodeApplyService
{
    public Task<string> ApplyCreateOrEditAsync(string workspaceRoot, Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        var filePath = parameters.TryGetValue("filePath", out var pathObj) ? pathObj?.ToString() ?? string.Empty : string.Empty;
        var fileName = parameters.TryGetValue("fileName", out var fileNameObj) ? fileNameObj?.ToString() ?? string.Empty : string.Empty;
        var content = parameters.TryGetValue("content", out var contentObj) ? contentObj?.ToString() ?? string.Empty : string.Empty;

        if (string.IsNullOrWhiteSpace(filePath) && !string.IsNullOrWhiteSpace(fileName))
        {
            filePath = string.IsNullOrWhiteSpace(workspaceRoot)
                ? fileName
                : Path.Combine(workspaceRoot, fileName);
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new InvalidOperationException("No file path was provided for code apply.");
        }

        var fullPath = Path.IsPathRooted(filePath) ? filePath : Path.Combine(workspaceRoot, filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? workspaceRoot);
        return File.WriteAllTextAsync(fullPath, content ?? string.Empty, ct).ContinueWith(_ => fullPath, ct);
    }
}

public sealed class CodeEditPlanner : ICodeEditPlanner
{
    private readonly ICodeDiffService _diffService;

    public CodeEditPlanner(ICodeDiffService diffService)
    {
        _diffService = diffService;
    }

    public void EnrichCodeArtifacts(AssistantTurnResult result, AssistantTurnRequest request, WorkspaceContextSnapshot snapshot)
    {
        if (!string.Equals(result.Intent, "create_file", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(result.Intent, "edit_file", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var fileName = result.Parameters.TryGetValue("fileName", out var fnObj) ? fnObj?.ToString() ?? string.Empty : string.Empty;
        var filePath = result.Parameters.TryGetValue("filePath", out var fpObj) ? fpObj?.ToString() ?? string.Empty : string.Empty;
        var content = result.Parameters.TryGetValue("content", out var contentObj) ? contentObj?.ToString() ?? string.Empty : string.Empty;
        var language = result.Parameters.TryGetValue("language", out var langObj) ? langObj?.ToString() ?? InferLanguage(fileName, filePath) : InferLanguage(fileName, filePath);
        var displayPath = !string.IsNullOrWhiteSpace(filePath) ? filePath : fileName;

        if (!string.IsNullOrWhiteSpace(content))
        {
            result.Artifacts.Add(new AssistantArtifact
            {
                Kind = AssistantArtifactKind.CodePreview,
                Title = string.IsNullOrWhiteSpace(displayPath) ? "Generated code" : displayPath,
                Summary = "Preview before apply",
                Content = content,
                Language = language,
                FilePath = displayPath,
                IsPreviewOnly = true
            });
        }

        if (string.Equals(result.Intent, "edit_file", StringComparison.OrdinalIgnoreCase))
        {
            var candidatePath = ResolveCandidatePath(snapshot.RootPath, filePath, fileName);
            var original = TryRead(candidatePath);
            if (!string.IsNullOrWhiteSpace(original) && !string.IsNullOrWhiteSpace(content))
            {
                result.Artifacts.Add(new AssistantArtifact
                {
                    Kind = AssistantArtifactKind.Diff,
                    Title = $"Diff: {Path.GetFileName(candidatePath)}",
                    Summary = "Review the proposed changes before apply",
                    Content = _diffService.BuildUnifiedDiff(original, content, candidatePath),
                    Language = "diff",
                    FilePath = candidatePath,
                    IsPreviewOnly = true
                });
            }
        }

        if (result.Artifacts.Count > 0)
        {
            result.RequiresConfirmation = true;
            result.ConfirmationMessage = string.IsNullOrWhiteSpace(result.ConfirmationMessage)
                ? "Review the generated code preview and diff before applying changes."
                : result.ConfirmationMessage;
        }
    }

    private static string ResolveCandidatePath(string workspaceRoot, string filePath, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            return Path.IsPathRooted(filePath) || string.IsNullOrWhiteSpace(workspaceRoot)
                ? filePath
                : Path.Combine(workspaceRoot, filePath);
        }

        return string.IsNullOrWhiteSpace(workspaceRoot)
            ? fileName
            : Path.Combine(workspaceRoot, fileName);
    }

    private static string TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string InferLanguage(string fileName, string filePath)
    {
        var ext = Path.GetExtension(string.IsNullOrWhiteSpace(filePath) ? fileName : filePath).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "csharp",
            ".xaml" => "xml",
            ".js" => "javascript",
            ".ts" => "typescript",
            ".py" => "python",
            ".json" => "json",
            ".html" => "html",
            ".css" => "css",
            ".ps1" => "powershell",
            _ => "text"
        };
    }
}

public sealed class ClipboardAttachmentService : IClipboardAttachmentService
{
    public AssistantAttachment? TryCreateImageAttachmentFromClipboard()
    {
        if (!Clipboard.ContainsImage())
        {
            return null;
        }

        var bitmap = Clipboard.GetImage();
        if (bitmap == null)
        {
            return null;
        }

        var attachmentsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZayFlow", "attachments");
        Directory.CreateDirectory(attachmentsRoot);

        var filePath = Path.Combine(attachmentsRoot, $"clipboard_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var fs = File.Create(filePath);
        encoder.Save(fs);

        return new AssistantAttachment
        {
            Title = Path.GetFileName(filePath),
            LocalPath = filePath,
            Width = bitmap.PixelWidth,
            Height = bitmap.PixelHeight,
            PreviewText = $"{bitmap.PixelWidth}x{bitmap.PixelHeight} clipboard image"
        };
    }
}

public sealed class ImagePreprocessService : IImagePreprocessService
{
    public AssistantAttachment Prepare(AssistantAttachment attachment)
    {
        if (string.IsNullOrWhiteSpace(attachment.LocalPath) || !File.Exists(attachment.LocalPath))
        {
            return attachment;
        }

        var source = new BitmapImage();
        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.UriSource = new Uri(attachment.LocalPath, UriKind.Absolute);
        source.EndInit();
        source.Freeze();

        var scale = source.PixelWidth > 1600 ? 1600d / source.PixelWidth : 1d;
        var transformed = scale < 0.999
            ? new TransformedBitmap(source, new ScaleTransform(scale, scale))
            : new TransformedBitmap(source, new ScaleTransform(1, 1));
        transformed.Freeze();

        var gray = new FormatConvertedBitmap();
        gray.BeginInit();
        gray.Source = transformed;
        gray.DestinationFormat = PixelFormats.Gray8;
        gray.EndInit();
        gray.Freeze();

        var processedRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZayFlow", "attachments", "processed");
        Directory.CreateDirectory(processedRoot);
        var processedPath = Path.Combine(processedRoot, $"{Path.GetFileNameWithoutExtension(attachment.LocalPath)}_processed.png");

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(gray));
        using (var fs = File.Create(processedPath))
        {
            encoder.Save(fs);
        }

        attachment.LocalPath = processedPath;
        attachment.Width = gray.PixelWidth;
        attachment.Height = gray.PixelHeight;
        attachment.IsCloudReady = true;
        attachment.PreviewText = $"{gray.PixelWidth}x{gray.PixelHeight} processed image";
        return attachment;
    }
}

public sealed class PowerShellOcrService : IOcrService
{
    public async Task<OcrResult> ExtractTextAsync(string imagePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return new OcrResult { Success = false };
        }

        var escaped = imagePath.Replace("'", "''", StringComparison.Ordinal);
        var script = """
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$path = '%PATH%'
$file = [Windows.Storage.StorageFile]::GetFileFromPathAsync($path).AsTask().Result
$stream = $file.OpenAsync([Windows.Storage.FileAccessMode]::Read).AsTask().Result
$decoder = [Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream).AsTask().Result
$bitmap = $decoder.GetSoftwareBitmapAsync().AsTask().Result
$engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
$result = $engine.RecognizeAsync($bitmap).AsTask().Result
$result.Text
""".Replace("%PATH%", escaped, StringComparison.Ordinal);

        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -Sta -Command \"{script.Replace("\"", "`\"")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
            {
                return new OcrResult { Success = false };
            }

            var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            var error = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return new OcrResult
                {
                    Success = false,
                    Summary = string.IsNullOrWhiteSpace(error) ? "Local OCR produced no text." : error.Trim()
                };
            }

            var text = output.Trim();
            var lines = text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
            return new OcrResult
            {
                Success = true,
                Text = text,
                Lines = lines,
                Summary = $"{lines.Count} OCR lines extracted",
                IsCodeLike = SimpleCodeHeuristics(lines)
            };
        }
        catch (Exception ex)
        {
            return new OcrResult { Success = false, Summary = $"Local OCR unavailable: {ex.Message}" };
        }
    }

    private static bool SimpleCodeHeuristics(IEnumerable<string> lines)
    {
        var joined = string.Join("\n", lines);
        return joined.Contains("using ", StringComparison.OrdinalIgnoreCase)
            || joined.Contains("class ", StringComparison.OrdinalIgnoreCase)
            || joined.Contains("public ", StringComparison.OrdinalIgnoreCase)
            || joined.Contains("def ", StringComparison.OrdinalIgnoreCase)
            || joined.Contains("function ", StringComparison.OrdinalIgnoreCase)
            || joined.Contains("{", StringComparison.Ordinal)
            || joined.Contains("}", StringComparison.Ordinal);
    }
}

public sealed class VisionAnalysisService : IVisionAnalysisService
{
    public VisionAnalysisResult Analyze(string text)
    {
        var normalized = text ?? string.Empty;
        var isCodeLike = normalized.Contains("using ", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("class ", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("def ", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("function ", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("{", StringComparison.Ordinal)
            || normalized.Contains("=>", StringComparison.Ordinal);

        return new VisionAnalysisResult
        {
            IsCodeLike = isCodeLike,
            Classification = isCodeLike ? "code_screenshot" : "document_image",
            Summary = isCodeLike ? "OCR text looks like source code." : "OCR text looks like a general document or UI screenshot."
        };
    }
}
