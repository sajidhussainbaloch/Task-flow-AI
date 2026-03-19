using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ZayFlow.App.Models;
using ZayFlow.App.Services.AI.Contracts;

namespace ZayFlow.App.Services;

public interface IFileIntelligenceService
{
    Task<(bool Success, string Error, string FileContent)> ReadSupportedFileAsync(string filePath, CancellationToken ct = default);
    Task<FileAnalysisResult> AnalyzeFileAsync(string fileName, string content, CancellationToken ct = default);
}

public sealed class FileIntelligenceService : IFileIntelligenceService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".pdf", ".docx", ".csv"
    };

    private readonly IAIService _aiService;

    public FileIntelligenceService(IAIService aiService)
    {
        _aiService = aiService;
    }

    public async Task<(bool Success, string Error, string FileContent)> ReadSupportedFileAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return (false, "File path cannot be empty.", string.Empty);
            }

            if (!File.Exists(filePath))
            {
                return (false, "File not found.", string.Empty);
            }

            var fileInfo = new FileInfo(filePath);
            const long maxFileSizeMB = 50;
            if (fileInfo.Length > maxFileSizeMB * 1024 * 1024)
            {
                return (false, $"File too large (max {maxFileSizeMB}MB).", string.Empty);
            }

            var extension = Path.GetExtension(filePath);
            if (!SupportedExtensions.Contains(extension))
            {
                return (false, "Unsupported file type. Supported: TXT, PDF, DOCX, CSV.", string.Empty);
            }

            return extension.ToLowerInvariant() switch
            {
                ".txt" => (true, string.Empty, await File.ReadAllTextAsync(filePath, ct)),
                ".csv" => (true, string.Empty, await File.ReadAllTextAsync(filePath, ct)),
                ".docx" => await ReadDocxWithErrorHandlingAsync(filePath, ct),
                ".pdf" => ReadPdfWithErrorHandling(filePath),
                _ => (false, "Unsupported file type.", string.Empty)
            };
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "Access denied to file.", string.Empty);
        }
        catch (IOException ex)
        {
            return (false, $"IO error: {ex.Message}", string.Empty);
        }
        catch (Exception ex)
        {
            return (false, $"Unexpected error: {ex.Message}", string.Empty);
        }
    }

    public async Task<FileAnalysisResult> AnalyzeFileAsync(string fileName, string content, CancellationToken ct = default)
    {
        var prompt = $"""
Analyze this file and produce a markdown response with exactly these headings:
## Summary
## Key Points
## Simplified Language
## Topic
## Notes

File name: {fileName}
Content:
{content}
""";

        var generated = await _aiService.GenerateTextAsync(prompt, ct);
        return ParseResult(generated);
    }

    private static async Task<(bool Success, string Error, string FileContent)> ReadDocxWithErrorHandlingAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var content = await ReadDocxAsync(filePath, ct);
            return (true, string.Empty, content);
        }
        catch (InvalidDataException)
        {
            return (false, "Invalid or corrupted DOCX file.", string.Empty);
        }
        catch (Exception ex)
        {
            return (false, $"Error reading DOCX: {ex.Message}", string.Empty);
        }
    }

    private static (bool Success, string Error, string FileContent) ReadPdfWithErrorHandling(string filePath)
    {
        try
        {
            var content = ReadPdf(filePath);
            return (true, string.Empty, content);
        }
        catch (Exception ex)
        {
            return (false, $"Error reading PDF: {ex.Message}", string.Empty);
        }
    }

    private static async Task<string> ReadDocxAsync(string filePath, CancellationToken ct)
    {
        using var stream = File.OpenRead(filePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("word/document.xml");
        if (entry == null)
        {
            return string.Empty;
        }

        await using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream, Encoding.UTF8);
        var xml = await reader.ReadToEndAsync();
        var withoutTags = Regex.Replace(xml, "<[^>]+>", " ");
        return Regex.Replace(withoutTags, "\\s+", " ").Trim();
    }

    private static string ReadPdf(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var text = Encoding.Latin1.GetString(bytes);
        var printable = new string(text.Where(ch => !char.IsControl(ch) || ch == '\n' || ch == '\r' || ch == '\t').ToArray());
        return Regex.Replace(printable, "\\s+", " ").Trim();
    }

    private static FileAnalysisResult ParseResult(string markdown)
    {
        string section(string header)
        {
            var pattern = $@"##\s*{Regex.Escape(header)}\s*(.*?)(?=\n##\s|\z)";
            var match = Regex.Match(markdown, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        }

        return new FileAnalysisResult
        {
            Summary = section("Summary"),
            KeyPoints = section("Key Points"),
            SimplifiedVersion = section("Simplified Language"),
            Topic = section("Topic"),
            Notes = section("Notes")
        };
    }
}
