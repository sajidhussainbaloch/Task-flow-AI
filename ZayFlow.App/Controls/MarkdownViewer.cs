using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZayFlow.App.Controls;

public class MarkdownViewer : RichTextBox
{
    public static readonly DependencyProperty MarkdownTextProperty = DependencyProperty.Register(
        nameof(MarkdownText),
        typeof(string),
        typeof(MarkdownViewer),
        new PropertyMetadata(string.Empty, OnMarkdownChanged));

    // Inline patterns (order matters — bold before italic)
    private static readonly Regex ImageRegex = new(@"!\[(?<alt>.*?)\]\((?<url>.*?)\)", RegexOptions.Compiled);
    private static readonly Regex LinkRegex = new(@"\[(?<text>[^\]]+)\]\((?<url>[^\)]+)\)", RegexOptions.Compiled);
    private static readonly Regex BoldItalicRegex = new(@"\*\*\*(.+?)\*\*\*", RegexOptions.Compiled);
    private static readonly Regex BoldRegex = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
    private static readonly Regex ItalicRegex = new(@"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", RegexOptions.Compiled);
    private static readonly Regex InlineCodeRegex = new(@"`([^`]+?)`", RegexOptions.Compiled);
    private static readonly Regex StrikethroughRegex = new(@"~~(.+?)~~", RegexOptions.Compiled);

    // Theme-aware colors (cached)
    private static readonly SolidColorBrush CodeBlockBg = new(Color.FromArgb(40, 99, 102, 241));
    private static readonly SolidColorBrush InlineCodeBg = new(Color.FromArgb(30, 99, 102, 241));
    private static readonly SolidColorBrush BlockquoteBorder = new(Color.FromRgb(99, 102, 241));
    private static readonly SolidColorBrush BlockquoteBg = new(Color.FromArgb(12, 99, 102, 241));
    private static readonly SolidColorBrush HRColor = new(Color.FromArgb(50, 150, 150, 160));
    private static readonly SolidColorBrush BulletColor = new(Color.FromRgb(99, 102, 241));
    private static readonly SolidColorBrush AccentBrush = new(Color.FromRgb(99, 102, 241));
    private static readonly FontFamily MonoFont = new("Cascadia Code, Consolas, Courier New");
    private static readonly FontFamily UIFont = new("Segoe UI");

    static MarkdownViewer()
    {
        CodeBlockBg.Freeze(); InlineCodeBg.Freeze(); BlockquoteBorder.Freeze();
        BlockquoteBg.Freeze(); HRColor.Freeze(); BulletColor.Freeze(); AccentBrush.Freeze();
    }

    public string MarkdownText
    {
        get => (string)GetValue(MarkdownTextProperty);
        set => SetValue(MarkdownTextProperty, value);
    }

    public MarkdownViewer()
    {
        IsReadOnly = true;
        IsDocumentEnabled = true;
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        SetResourceReference(ForegroundProperty, "PrimaryTextBrush");
    }

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownViewer viewer)
        {
            var doc = BuildDocument(e.NewValue?.ToString() ?? string.Empty);
            doc.SetResourceReference(FlowDocument.ForegroundProperty, "PrimaryTextBrush");
            viewer.Document = doc;
        }
    }

    private static FlowDocument BuildDocument(string markdown)
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            TextAlignment = TextAlignment.Left,
            Background = Brushes.Transparent,
            FontSize = 13.5,
            FontFamily = UIFont
        };

        if (string.IsNullOrWhiteSpace(markdown))
        {
            doc.Blocks.Add(new Paragraph(new Run(string.Empty)));
            return doc;
        }

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var isCodeBlock = false;
        var codeBuffer = new List<string>();
        var codeLang = string.Empty;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i] ?? string.Empty;

            // ── Fenced code blocks ──
            if (line.TrimStart().StartsWith("```"))
            {
                if (!isCodeBlock)
                {
                    isCodeBlock = true;
                    codeLang = line.TrimStart().Length > 3 ? line.TrimStart()[3..].Trim() : "";
                    codeBuffer.Clear();
                }
                else
                {
                    isCodeBlock = false;
                    var code = string.Join(Environment.NewLine, codeBuffer);

                    // Language label + code block
                    var section = new System.Windows.Documents.Section
                    {
                        Margin = new Thickness(0, 6, 0, 10),
                        Padding = new Thickness(0)
                    };

                    if (!string.IsNullOrWhiteSpace(codeLang))
                    {
                        var langLabel = new Paragraph(new Run(codeLang.ToUpperInvariant())
                        {
                            FontSize = 10,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = AccentBrush
                        })
                        {
                            Background = CodeBlockBg,
                            Margin = new Thickness(0, 0, 0, 0),
                            Padding = new Thickness(12, 4, 12, 2),
                            FontFamily = UIFont
                        };
                        section.Blocks.Add(langLabel);
                    }

                    var codeParagraph = new Paragraph(new Run(code))
                    {
                        FontFamily = MonoFont,
                        FontSize = 12.5,
                        Background = CodeBlockBg,
                        Margin = new Thickness(0),
                        Padding = new Thickness(12, 8, 12, 10),
                        LineHeight = 18
                    };
                    section.Blocks.Add(codeParagraph);
                    doc.Blocks.Add(section);
                }
                continue;
            }

            if (isCodeBlock)
            {
                codeBuffer.Add(line);
                continue;
            }

            var trimmed = line.Trim();

            // ── Empty line ──
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                doc.Blocks.Add(new Paragraph(new Run(string.Empty)) { Margin = new Thickness(0, 2, 0, 2) });
                continue;
            }

            // ── Horizontal rule (---, ***, ___) ──
            if (Regex.IsMatch(trimmed, @"^[-*_]{3,}$"))
            {
                var hr = new Paragraph(new Run(" "))
                {
                    Margin = new Thickness(0, 6, 0, 6),
                    BorderBrush = HRColor,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    FontSize = 1
                };
                doc.Blocks.Add(hr);
                continue;
            }

            // ── Headers ──
            if (trimmed.StartsWith("#"))
            {
                var level = trimmed.TakeWhile(c => c == '#').Count();
                if (level <= 6 && trimmed.Length > level && trimmed[level] == ' ')
                {
                    var text = trimmed[(level + 1)..];
                    var size = level switch { 1 => 22d, 2 => 19d, 3 => 16.5, _ => 14.5 };
                    var header = new Paragraph { FontSize = size, Margin = new Thickness(0, 6, 0, 6) };
                    header.FontWeight = level <= 2 ? FontWeights.Bold : FontWeights.SemiBold;
                    AddInlineFormatting(header.Inlines, text);
                    doc.Blocks.Add(header);
                    continue;
                }
            }

            // ── Blockquote ──
            if (trimmed.StartsWith(">"))
            {
                var quoteLines = new List<string>();
                for (; i < lines.Length; i++)
                {
                    var ql = (lines[i] ?? "").Trim();
                    if (ql.StartsWith(">"))
                        quoteLines.Add(ql.TrimStart('>', ' '));
                    else { i--; break; }
                }
                var quoteSection = new System.Windows.Documents.Section
                {
                    Margin = new Thickness(0, 4, 0, 4),
                    Padding = new Thickness(14, 6, 8, 6),
                    BorderBrush = BlockquoteBorder,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Background = BlockquoteBg
                };
                foreach (var ql in quoteLines)
                {
                    var qp = new Paragraph { Margin = new Thickness(0, 1, 0, 1), FontStyle = FontStyles.Italic };
                    AddInlineFormatting(qp.Inlines, ql);
                    quoteSection.Blocks.Add(qp);
                }
                doc.Blocks.Add(quoteSection);
                continue;
            }

            // ── Unordered list (- or * or +) ──
            if (Regex.IsMatch(trimmed, @"^[-*+]\s"))
            {
                var listBlock = new System.Windows.Documents.List
                {
                    MarkerStyle = TextMarkerStyle.None,
                    Margin = new Thickness(0, 2, 0, 2),
                    Padding = new Thickness(16, 0, 0, 0)
                };
                for (; i < lines.Length; i++)
                {
                    var ll = (lines[i] ?? "").Trim();
                    if (Regex.IsMatch(ll, @"^[-*+]\s"))
                    {
                        var itemText = ll[2..];
                        var itemPara = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
                        itemPara.Inlines.Add(new Run("•  ") { Foreground = BulletColor, FontWeight = FontWeights.Bold });
                        AddInlineFormatting(itemPara.Inlines, itemText);
                        listBlock.ListItems.Add(new ListItem(itemPara));
                    }
                    else { i--; break; }
                }
                doc.Blocks.Add(listBlock);
                continue;
            }

            // ── Ordered list (1. 2. 3.) ──
            if (Regex.IsMatch(trimmed, @"^\d+\.\s"))
            {
                var listBlock = new System.Windows.Documents.List
                {
                    MarkerStyle = TextMarkerStyle.None,
                    Margin = new Thickness(0, 2, 0, 2),
                    Padding = new Thickness(16, 0, 0, 0)
                };
                int counter = 1;
                for (; i < lines.Length; i++)
                {
                    var ll = (lines[i] ?? "").Trim();
                    var numMatch = Regex.Match(ll, @"^\d+\.\s(.*)");
                    if (numMatch.Success)
                    {
                        var itemText = numMatch.Groups[1].Value;
                        var itemPara = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
                        itemPara.Inlines.Add(new Run($"{counter}.  ") { Foreground = BulletColor, FontWeight = FontWeights.SemiBold });
                        AddInlineFormatting(itemPara.Inlines, itemText);
                        listBlock.ListItems.Add(new ListItem(itemPara));
                        counter++;
                    }
                    else { i--; break; }
                }
                doc.Blocks.Add(listBlock);
                continue;
            }

            // ── Table (pipe-separated) ──
            if (trimmed.Contains('|') && trimmed.Count(c => c == '|') >= 2)
            {
                doc.Blocks.Add(new Paragraph(new Run(trimmed))
                {
                    FontFamily = MonoFont,
                    FontSize = 12,
                    Margin = new Thickness(0, 2, 0, 2)
                });
                continue;
            }

            // ── Image ──
            var imageMatch = ImageRegex.Match(trimmed);
            if (imageMatch.Success && imageMatch.Index == 0)
            {
                var alt = imageMatch.Groups["alt"].Value;
                var url = imageMatch.Groups["url"].Value;

                if (TryLoadImage(url, out var bitmapImage))
                {
                    var image = new System.Windows.Controls.Image
                    {
                        Source = bitmapImage,
                        MaxWidth = 320,
                        MaxHeight = 320,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(0, 6, 0, 6)
                    };
                    var border = new Border
                    {
                        CornerRadius = new CornerRadius(8),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(40, 99, 102, 241)),
                        BorderThickness = new Thickness(1),
                        Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)),
                        Padding = new Thickness(8),
                        Child = image
                    };
                    doc.Blocks.Add(new BlockUIContainer(border) { Margin = new Thickness(0, 4, 0, 4) });
                }
                else
                {
                    doc.Blocks.Add(new Paragraph(new Run($"[Image: {alt}] {url}"))
                    {
                        Foreground = AccentBrush,
                        Margin = new Thickness(0, 0, 0, 4)
                    });
                }
                continue;
            }

            // ── Normal paragraph with inline formatting ──
            var paragraph = new Paragraph { Margin = new Thickness(0, 1, 0, 3), LineHeight = 21 };
            AddInlineFormatting(paragraph.Inlines, trimmed);
            doc.Blocks.Add(paragraph);
        }

        return doc;
    }

    /// <summary>
    /// Parses inline markdown: **bold**, *italic*, ***bold+italic***, `inline code`, ~~strikethrough~~, [links](url)
    /// </summary>
    private static void AddInlineFormatting(InlineCollection inlines, string text)
    {
        // Build a list of (index, length, type, content) for all inline tokens
        var tokens = new List<(int Index, int Length, string Type, string Content, string Extra)>();

        foreach (Match m in BoldItalicRegex.Matches(text))
            tokens.Add((m.Index, m.Length, "bolditalic", m.Groups[1].Value, ""));
        foreach (Match m in BoldRegex.Matches(text))
            tokens.Add((m.Index, m.Length, "bold", m.Groups[1].Value, ""));
        foreach (Match m in ItalicRegex.Matches(text))
            tokens.Add((m.Index, m.Length, "italic", m.Groups[1].Value, ""));
        foreach (Match m in InlineCodeRegex.Matches(text))
            tokens.Add((m.Index, m.Length, "code", m.Groups[1].Value, ""));
        foreach (Match m in StrikethroughRegex.Matches(text))
            tokens.Add((m.Index, m.Length, "strike", m.Groups[1].Value, ""));
        foreach (Match m in LinkRegex.Matches(text))
            tokens.Add((m.Index, m.Length, "link", m.Groups["text"].Value, m.Groups["url"].Value));
        foreach (Match m in ImageRegex.Matches(text))
            tokens.Add((m.Index, m.Length, "image", m.Groups["alt"].Value, m.Groups["url"].Value));

        // Remove overlapping tokens — keep the one that starts first (or is longest)
        tokens.Sort((a, b) => a.Index != b.Index ? a.Index.CompareTo(b.Index) : b.Length.CompareTo(a.Length));
        var filtered = new List<(int Index, int Length, string Type, string Content, string Extra)>();
        int lastEnd = 0;
        foreach (var t in tokens)
        {
            if (t.Index >= lastEnd)
            {
                filtered.Add(t);
                lastEnd = t.Index + t.Length;
            }
        }

        int pos = 0;
        foreach (var token in filtered)
        {
            // Plain text before this token
            if (token.Index > pos)
            {
                inlines.Add(new Run(text[pos..token.Index]));
            }

            switch (token.Type)
            {
                case "bolditalic":
                    inlines.Add(new Run(token.Content) { FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic });
                    break;
                case "bold":
                    inlines.Add(new Run(token.Content) { FontWeight = FontWeights.Bold });
                    break;
                case "italic":
                    inlines.Add(new Run(token.Content) { FontStyle = FontStyles.Italic });
                    break;
                case "code":
                    inlines.Add(new Run($" {token.Content} ")
                    {
                        FontFamily = MonoFont,
                        FontSize = 12,
                        Background = InlineCodeBg,
                        Foreground = AccentBrush
                    });
                    break;
                case "strike":
                    inlines.Add(new Run(token.Content) { TextDecorations = TextDecorations.Strikethrough });
                    break;
                case "link":
                    if (Uri.TryCreate(token.Extra, UriKind.Absolute, out var uri))
                    {
                        var hl = new Hyperlink(new Run(token.Content)) { NavigateUri = uri };
                        var linkUrl = token.Extra;
                        hl.RequestNavigate += (_, _) =>
                        {
                            Process.Start(new ProcessStartInfo { FileName = linkUrl, UseShellExecute = true });
                        };
                        inlines.Add(hl);
                    }
                    else
                    {
                        inlines.Add(new Run(token.Content) { Foreground = AccentBrush });
                    }
                    break;
                case "image":
                    inlines.Add(new Run($"[Image: {token.Content}]") { Foreground = AccentBrush });
                    break;
            }

            pos = token.Index + token.Length;
        }

        // Remaining text after last token
        if (pos < text.Length)
        {
            inlines.Add(new Run(text[pos..]));
        }
    }

    /// <summary>
    /// Try to load a local image file as a BitmapImage.
    /// </summary>
    private static bool TryLoadImage(string path, out BitmapImage? image)
    {
        image = null;
        try
        {
            var cleanPath = path.Trim().Trim('"', '\'');
            if (!File.Exists(cleanPath)) return false;

            var ext = Path.GetExtension(cleanPath).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".ico" or ".webp" or ".tiff"))
                return false;

            image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(cleanPath, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return true;
        }
        catch
        {
            image = null;
            return false;
        }
    }
}