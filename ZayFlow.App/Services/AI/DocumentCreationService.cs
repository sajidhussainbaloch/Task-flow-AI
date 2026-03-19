using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace ZayFlow.App.Services.AI;

/// <summary>
/// Creates .docx (Office Open XML) documents that open natively in Microsoft Word.
/// Uses raw OOXML zip format — no external NuGet packages needed.
/// </summary>
public class DocumentCreationService
{
    private readonly ILogger<DocumentCreationService> _logger;

    // XML namespaces for OOXML
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace CT = "http://schemas.openxmlformats.org/package/2006/content-types";

    public DocumentCreationService(ILogger<DocumentCreationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Create a real .docx file that opens in Microsoft Word.
    /// </summary>
    public async Task<(bool Success, string FilePath, string Message)> CreateFormattedDocumentAsync(
        string title,
        string content,
        string documentType = "document")
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var fileName = $"{SanitizeFileName(documentType)}_{timestamp}.docx";
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var filePath = Path.Combine(desktopPath, fileName);

            await Task.Run(() => CreateDocxFile(filePath, title, content));

            _logger.LogInformation($"Created .docx: {filePath}");
            return (true, filePath, $"Document created: {fileName}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Document creation error: {ex.Message}");
            return (false, "", $"Failed to create document: {ex.Message}");
        }
    }

    /// <summary>
    /// Create a professional timetable/schedule document.
    /// </summary>
    public async Task<(bool Success, string FilePath, string Message)> CreateTimetableAsync(
        string title,
        Dictionary<string, List<string>> schedule)
    {
        try
        {
            var content = new StringBuilder();
            foreach (var day in schedule)
            {
                content.AppendLine($"{day.Key.ToUpper()}");
                foreach (var activity in day.Value)
                    content.AppendLine($"  - {activity}");
                content.AppendLine();
            }
            return await CreateFormattedDocumentAsync(title, content.ToString(), "timetable");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Timetable creation error: {ex.Message}");
            return (false, "", $"Failed to create timetable: {ex.Message}");
        }
    }

    /// <summary>
    /// Create a structured checklist document.
    /// </summary>
    public async Task<(bool Success, string FilePath, string Message)> CreateChecklistAsync(
        string title,
        List<string> items)
    {
        try
        {
            var content = new StringBuilder();
            foreach (var item in items)
                content.AppendLine($"[ ] {item}");
            return await CreateFormattedDocumentAsync(title, content.ToString(), "checklist");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Checklist creation error: {ex.Message}");
            return (false, "", $"Failed to create checklist: {ex.Message}");
        }
    }

    /// <summary>
    /// Create a structured report document.
    /// </summary>
    public async Task<(bool Success, string FilePath, string Message)> CreateReportAsync(
        string title,
        string summary,
        Dictionary<string, string> sections)
    {
        try
        {
            var content = new StringBuilder();
            content.AppendLine($"Summary: {summary}\n");
            foreach (var section in sections)
            {
                content.AppendLine($"{section.Key.ToUpper()}");
                content.AppendLine(section.Value);
                content.AppendLine();
            }
            return await CreateFormattedDocumentAsync(title, content.ToString(), "report");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Report creation error: {ex.Message}");
            return (false, "", $"Failed to create report: {ex.Message}");
        }
    }

    /// <summary>
    /// Create a CSV file.
    /// </summary>
    public async Task<(bool Success, string FilePath, string Message)> CreateCSVAsync(
        string title,
        List<string> headers,
        List<List<string>> rows)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var fileName = $"{SanitizeFileName(title)}_{timestamp}.csv";
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var filePath = Path.Combine(desktopPath, fileName);

            var csv = new StringBuilder();
            csv.AppendLine(string.Join(",", headers.Select(EscapeCSV)));
            foreach (var row in rows)
                csv.AppendLine(string.Join(",", row.Select(EscapeCSV)));

            await File.WriteAllTextAsync(filePath, csv.ToString());
            return (true, filePath, $"Spreadsheet created: {fileName}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"CSV creation error: {ex.Message}");
            return (false, "", $"Failed to create spreadsheet: {ex.Message}");
        }
    }

    // ─── DOCX generation using raw OOXML zip format ──────────────────────────

    private void CreateDocxFile(string filePath, string title, string content)
    {
        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        // [Content_Types].xml
        AddZipEntry(zip, "[Content_Types].xml", BuildContentTypes());

        // _rels/.rels
        AddZipEntry(zip, "_rels/.rels", BuildRootRels());

        // word/_rels/document.xml.rels
        AddZipEntry(zip, "word/_rels/document.xml.rels", BuildDocumentRels());

        // word/document.xml — the actual content
        AddZipEntry(zip, "word/document.xml", BuildDocumentXml(title, content));

        // word/styles.xml — basic styles
        AddZipEntry(zip, "word/styles.xml", BuildStylesXml());
    }

    private string BuildContentTypes()
    {
        var doc = new XDocument(
            new XElement(CT + "Types",
                new XElement(CT + "Default",
                    new XAttribute("Extension", "rels"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(CT + "Default",
                    new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/xml")),
                new XElement(CT + "Override",
                    new XAttribute("PartName", "/word/document.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")),
                new XElement(CT + "Override",
                    new XAttribute("PartName", "/word/styles.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"))
            )
        );
        return doc.Declaration?.ToString() + doc.ToString() ?? doc.ToString();
    }

    private string BuildRootRels()
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(Rel + "Relationships",
                new XElement(Rel + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "word/document.xml"))
            )
        );
        return doc.Declaration + doc.ToString();
    }

    private string BuildDocumentRels()
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(Rel + "Relationships",
                new XElement(Rel + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
                    new XAttribute("Target", "styles.xml"))
            )
        );
        return doc.Declaration + doc.ToString();
    }

    private string BuildDocumentXml(string title, string content)
    {
        var body = new XElement(W + "body");

        // Title paragraph (Heading1 style)
        if (!string.IsNullOrWhiteSpace(title))
        {
            body.Add(new XElement(W + "p",
                new XElement(W + "pPr",
                    new XElement(W + "pStyle", new XAttribute(W + "val", "Heading1"))),
                new XElement(W + "r",
                    new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), title))
            ));
        }

        // Date line
        body.Add(new XElement(W + "p",
            new XElement(W + "pPr",
                new XElement(W + "rPr",
                    new XElement(W + "color", new XAttribute(W + "val", "808080")),
                    new XElement(W + "sz", new XAttribute(W + "val", "20")))),
            new XElement(W + "r",
                new XElement(W + "rPr",
                    new XElement(W + "color", new XAttribute(W + "val", "808080")),
                    new XElement(W + "sz", new XAttribute(W + "val", "20"))),
                new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"),
                    $"Created: {DateTime.Now:f}"))
        ));

        // Empty line
        body.Add(new XElement(W + "p"));

        // Content paragraphs — split on newlines
        var lines = content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            body.Add(new XElement(W + "p",
                new XElement(W + "r",
                    new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), line))
            ));
        }

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(W + "document",
                new XAttribute(XNamespace.Xmlns + "w", W),
                new XAttribute(XNamespace.Xmlns + "r", R),
                body
            )
        );
        return doc.Declaration + doc.ToString();
    }

    private string BuildStylesXml()
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(W + "styles",
                new XAttribute(XNamespace.Xmlns + "w", W),
                // Default document font
                new XElement(W + "docDefaults",
                    new XElement(W + "rPrDefault",
                        new XElement(W + "rPr",
                            new XElement(W + "rFonts",
                                new XAttribute(W + "ascii", "Calibri"),
                                new XAttribute(W + "hAnsi", "Calibri")),
                            new XElement(W + "sz", new XAttribute(W + "val", "24"))))),
                // Heading1 style
                new XElement(W + "style",
                    new XAttribute(W + "type", "paragraph"),
                    new XAttribute(W + "styleId", "Heading1"),
                    new XElement(W + "name", new XAttribute(W + "val", "heading 1")),
                    new XElement(W + "rPr",
                        new XElement(W + "b"),
                        new XElement(W + "sz", new XAttribute(W + "val", "36")),
                        new XElement(W + "color", new XAttribute(W + "val", "2E74B5")))
                )
            )
        );
        return doc.Declaration + doc.ToString();
    }

    private static void AddZipEntry(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name
            .Where(c => !invalid.Contains(c))
            .Take(50)
            .ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "document" : sanitized;
    }

    private string EscapeCSV(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
            return $"\"{field.Replace("\"", "\"\"")}\"";
        return field;
    }
}
