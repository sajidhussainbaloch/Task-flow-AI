using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Contracts;

namespace ZayFlow.App.Services.AI.Handlers;

/// <summary>
/// Tier 4: Media and data tools — text extraction, PDF tools, image tools, data conversion, text transformation.
/// </summary>
public class MediaHandler
{
    private readonly ILogger<MediaHandler> _logger;

    public MediaHandler(ILogger<MediaHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>data_convert — Convert between data formats (JSON↔CSV↔XML↔YAML).</summary>
    public async Task<ActionResult> ExecuteDataConvertAsync(string resolvedFilePath, string targetFormat, CancellationToken ct)
    {
        if (!File.Exists(resolvedFilePath))
            return new ActionResult { Success = false, Message = $"File not found: {resolvedFilePath}" };

        var sourceExt = Path.GetExtension(resolvedFilePath).ToLowerInvariant();
        var content = await File.ReadAllTextAsync(resolvedFilePath, ct);
        string outputContent;
        string outputExt;

        try
        {
            switch ($"{sourceExt}→{targetFormat.ToLowerInvariant()}")
            {
                case ".json→csv":
                    var jsonArr = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, System.Text.Json.JsonElement>>>(content);
                    if (jsonArr == null || jsonArr.Count == 0) return new ActionResult { Success = false, Message = "JSON must be an array of objects." };
                    var headers = jsonArr.SelectMany(o => o.Keys).Distinct().ToList();
                    var csvSb = new StringBuilder();
                    csvSb.AppendLine(string.Join(",", headers));
                    foreach (var obj in jsonArr)
                        csvSb.AppendLine(string.Join(",", headers.Select(h => obj.TryGetValue(h, out var v) ? $"\"{v}\"" : "")));
                    outputContent = csvSb.ToString();
                    outputExt = ".csv";
                    break;

                case ".csv→json":
                    var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length < 2) return new ActionResult { Success = false, Message = "CSV must have header + at least 1 data row." };
                    var csvHeaders = lines[0].Trim().Split(',').Select(h => h.Trim('"', ' ')).ToList();
                    var jsonList = new List<Dictionary<string, string>>();
                    for (int i = 1; i < lines.Length; i++)
                    {
                        var values = lines[i].Trim().Split(',').Select(v => v.Trim('"', ' ')).ToList();
                        var obj = new Dictionary<string, string>();
                        for (int j = 0; j < Math.Min(csvHeaders.Count, values.Count); j++)
                            obj[csvHeaders[j]] = values[j];
                        jsonList.Add(obj);
                    }
                    outputContent = System.Text.Json.JsonSerializer.Serialize(jsonList, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    outputExt = ".json";
                    break;

                case ".json→xml":
                    outputContent = $"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<root>\n{JsonToXmlSimple(content)}\n</root>";
                    outputExt = ".xml";
                    break;

                default:
                    return new ActionResult { Success = false, Message = $"Unsupported conversion: {sourceExt} → {targetFormat}. Supported: json↔csv, json→xml" };
            }
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = $"Conversion failed: {ex.Message}" };
        }

        var outputPath = Path.ChangeExtension(resolvedFilePath, outputExt);
        await File.WriteAllTextAsync(outputPath, outputContent, ct);
        return new ActionResult { Success = true, Message = $"✅ Converted {Path.GetFileName(resolvedFilePath)} → {Path.GetFileName(outputPath)}" };
    }

    private string JsonToXmlSimple(string json)
    {
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var sb = new StringBuilder();
            JsonElementToXml(doc.RootElement, sb, "item", 1);
            return sb.ToString();
        }
        catch { return $"  <error>Could not parse JSON</error>"; }
    }

    private void JsonElementToXml(System.Text.Json.JsonElement element, StringBuilder sb, string name, int depth)
    {
        var indent = new string(' ', depth * 2);
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                sb.AppendLine($"{indent}<{name}>");
                foreach (var prop in element.EnumerateObject())
                    JsonElementToXml(prop.Value, sb, prop.Name, depth + 1);
                sb.AppendLine($"{indent}</{name}>");
                break;
            case System.Text.Json.JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    JsonElementToXml(item, sb, name, depth);
                break;
            default:
                sb.AppendLine($"{indent}<{name}>{System.Security.SecurityElement.Escape(element.ToString())}</{name}>");
                break;
        }
    }

    /// <summary>text_transform — Apply transformations to text files (uppercase, lowercase, sort lines, deduplicate, trim, etc.).</summary>
    public async Task<ActionResult> ExecuteTextTransformAsync(string resolvedFilePath, string operation, CancellationToken ct)
    {
        if (!File.Exists(resolvedFilePath))
            return new ActionResult { Success = false, Message = $"File not found: {resolvedFilePath}" };

        var content = await File.ReadAllTextAsync(resolvedFilePath, ct);
        var lines = content.Split('\n');

        string result;
        string description;

        switch (operation.ToLowerInvariant())
        {
            case "uppercase" or "upper":
                result = content.ToUpperInvariant();
                description = "Converted to UPPERCASE";
                break;
            case "lowercase" or "lower":
                result = content.ToLowerInvariant();
                description = "Converted to lowercase";
                break;
            case "sort" or "sort_lines":
                result = string.Join('\n', lines.OrderBy(l => l));
                description = $"Sorted {lines.Length} lines alphabetically";
                break;
            case "reverse" or "reverse_lines":
                result = string.Join('\n', lines.AsEnumerable().Reverse());
                description = "Reversed line order";
                break;
            case "deduplicate" or "unique":
                var unique = lines.Distinct().ToArray();
                result = string.Join('\n', unique);
                description = $"Removed {lines.Length - unique.Length} duplicate lines ({unique.Length} remaining)";
                break;
            case "trim":
                result = string.Join('\n', lines.Select(l => l.TrimEnd()));
                description = "Trimmed trailing whitespace";
                break;
            case "number" or "number_lines":
                result = string.Join('\n', lines.Select((l, i) => $"{i + 1,4}: {l}"));
                description = $"Added line numbers to {lines.Length} lines";
                break;
            case "remove_empty" or "compact":
                var nonEmpty = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
                result = string.Join('\n', nonEmpty);
                description = $"Removed {lines.Length - nonEmpty.Length} empty lines";
                break;
            case "stats":
                var charCount = content.Length;
                var wordCount = content.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
                return new ActionResult
                {
                    Success = true,
                    Message = $"📊 Text Stats: {Path.GetFileName(resolvedFilePath)}\n  Lines: {lines.Length}\n  Words: {wordCount}\n  Characters: {charCount}\n  Size: {new FileInfo(resolvedFilePath).Length / 1024}KB"
                };
            default:
                return new ActionResult { Success = false, Message = $"Unknown operation: {operation}. Available: uppercase, lowercase, sort, reverse, deduplicate, trim, number, remove_empty, stats" };
        }

        var outputPath = Path.Combine(
            Path.GetDirectoryName(resolvedFilePath)!,
            $"{Path.GetFileNameWithoutExtension(resolvedFilePath)}_{operation}{Path.GetExtension(resolvedFilePath)}");
        await File.WriteAllTextAsync(outputPath, result, ct);

        return new ActionResult { Success = true, Message = $"✅ {description}\n📄 Saved to: {Path.GetFileName(outputPath)}" };
    }

    /// <summary>image_tools — Basic image operations (info, resize concept).</summary>
    public Task<ActionResult> ExecuteImageToolsAsync(string resolvedFilePath, string operation, CancellationToken ct)
    {
        if (!File.Exists(resolvedFilePath))
            return Task.FromResult(new ActionResult { Success = false, Message = $"File not found: {resolvedFilePath}" });

        switch (operation.ToLowerInvariant())
        {
            case "info" or "metadata":
                var fi = new FileInfo(resolvedFilePath);
                try
                {
                    using var bmp = new System.Drawing.Bitmap(resolvedFilePath);
                    return Task.FromResult(new ActionResult
                    {
                        Success = true,
                        Message = $"🖼️ Image Info: {fi.Name}\n  Dimensions: {bmp.Width}x{bmp.Height}\n  Format: {bmp.RawFormat}\n  Size: {fi.Length / 1024}KB\n  Modified: {fi.LastWriteTime:f}"
                    });
                }
                catch
                {
                    return Task.FromResult(new ActionResult
                    {
                        Success = true,
                        Message = $"🖼️ File Info: {fi.Name}\n  Size: {fi.Length / 1024}KB\n  Extension: {fi.Extension}\n  Modified: {fi.LastWriteTime:f}"
                    });
                }

            default:
                return Task.FromResult(new ActionResult { Success = false, Message = "Available image operations: info" });
        }
    }

    /// <summary>pdf_tools — Basic PDF info (page count, size).</summary>
    public Task<ActionResult> ExecutePdfToolsAsync(string resolvedFilePath, string operation, CancellationToken ct)
    {
        if (!File.Exists(resolvedFilePath))
            return Task.FromResult(new ActionResult { Success = false, Message = $"File not found: {resolvedFilePath}" });

        var fi = new FileInfo(resolvedFilePath);
        if (!fi.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new ActionResult { Success = false, Message = "File is not a PDF." });

        // Basic PDF info without external library
        try
        {
            var bytes = File.ReadAllBytes(resolvedFilePath);
            var text = Encoding.ASCII.GetString(bytes);
            var pageCount = System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page[^s]").Count;

            return Task.FromResult(new ActionResult
            {
                Success = true,
                Message = $"📑 PDF Info: {fi.Name}\n  Size: {fi.Length / 1024}KB\n  Estimated pages: ~{Math.Max(1, pageCount)}\n  Modified: {fi.LastWriteTime:f}"
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ActionResult { Success = false, Message = $"PDF analysis failed: {ex.Message}" });
        }
    }

    /// <summary>extract_text — Extract readable text from a file.</summary>
    public async Task<ActionResult> ExecuteExtractTextAsync(string resolvedFilePath, CancellationToken ct)
    {
        if (!File.Exists(resolvedFilePath))
            return new ActionResult { Success = false, Message = $"File not found: {resolvedFilePath}" };

        var ext = Path.GetExtension(resolvedFilePath).ToLowerInvariant();
        var fi = new FileInfo(resolvedFilePath);

        if (fi.Length > 5 * 1024 * 1024)
            return new ActionResult { Success = false, Message = "File too large (>5MB). Extract text from smaller files." };

        try
        {
            string extractedText;
            switch (ext)
            {
                case ".txt" or ".md" or ".csv" or ".log" or ".json" or ".xml" or ".yaml" or ".yml":
                    extractedText = await File.ReadAllTextAsync(resolvedFilePath, ct);
                    break;
                case ".html" or ".htm":
                    var html = await File.ReadAllTextAsync(resolvedFilePath, ct);
                    extractedText = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
                    extractedText = System.Text.RegularExpressions.Regex.Replace(extractedText, @"\s+", " ").Trim();
                    break;
                default:
                    // Try reading as text
                    var bytes = await File.ReadAllBytesAsync(resolvedFilePath, ct);
                    extractedText = Encoding.UTF8.GetString(bytes);
                    // Check if it's actually readable
                    var printableRatio = (double)extractedText.Count(c => !char.IsControl(c) || c == '\n' || c == '\r' || c == '\t') / extractedText.Length;
                    if (printableRatio < 0.8)
                        return new ActionResult { Success = false, Message = $"Cannot extract text from binary file ({ext})." };
                    break;
            }

            if (extractedText.Length > 5000)
                extractedText = extractedText[..5000] + "\n\n... (truncated)";

            var wordCount = extractedText.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
            return new ActionResult
            {
                Success = true,
                Message = $"📄 Extracted text from {fi.Name} ({wordCount} words):\n\n{extractedText}"
            };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = $"Text extraction failed: {ex.Message}" };
        }
    }
}
