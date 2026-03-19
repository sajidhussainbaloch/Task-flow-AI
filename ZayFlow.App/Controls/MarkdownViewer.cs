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

    private static readonly Regex LinkRegex = new(@"\[(?<text>.*?)\]\((?<url>.*?)\)", RegexOptions.Compiled);
    private static readonly Regex ImageRegex = new(@"!\[(?<alt>.*?)\]\((?<url>.*?)\)", RegexOptions.Compiled);

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
        
        // Use theme-aware foreground color for dark mode support
        SetResourceReference(ForegroundProperty, "PrimaryTextBrush");
    }

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownViewer viewer)
        {
            viewer.Document = BuildDocument(e.NewValue?.ToString() ?? string.Empty);
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
            FontFamily = new FontFamily("Segoe UI")
        };

        if (string.IsNullOrWhiteSpace(markdown))
        {
            doc.Blocks.Add(new Paragraph(new Run(string.Empty)));
            return doc;
        }

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var isCodeBlock = false;
        var codeBuffer = new List<string>();

        foreach (var raw in lines)
        {
            var line = raw ?? string.Empty;

            if (line.TrimStart().StartsWith("```"))
            {
                if (!isCodeBlock)
                {
                    isCodeBlock = true;
                    codeBuffer.Clear();
                }
                else
                {
                    isCodeBlock = false;
                    var code = string.Join(Environment.NewLine, codeBuffer);
                    var codeParagraph = new Paragraph(new Run(code))
                    {
                        FontFamily = new FontFamily("Consolas"),
                        Background = new SolidColorBrush(Color.FromArgb(35, 99, 102, 241)),
                        Margin = new Thickness(0, 4, 0, 8),
                        Padding = new Thickness(8)
                    };
                    doc.Blocks.Add(codeParagraph);
                }

                continue;
            }

            if (isCodeBlock)
            {
                codeBuffer.Add(line);
                continue;
            }

            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                doc.Blocks.Add(new Paragraph(new Run(string.Empty)) { Margin = new Thickness(0, 2, 0, 2) });
                continue;
            }

            if (trimmed.StartsWith("#"))
            {
                var level = trimmed.TakeWhile(c => c == '#').Count();
                var text = trimmed.TrimStart('#', ' ');
                var size = level switch
                {
                    1 => 20d,
                    2 => 18d,
                    3 => 16d,
                    _ => 14d
                };

                var header = new Paragraph(new Run(text))
                {
                    FontWeight = FontWeights.SemiBold,
                    FontSize = size,
                    Margin = new Thickness(0, 4, 0, 6)
                };
                doc.Blocks.Add(header);
                continue;
            }

            if (trimmed.Contains('|') && trimmed.Count(c => c == '|') >= 2)
            {
                doc.Blocks.Add(new Paragraph(new Run(trimmed))
                {
                    FontFamily = new FontFamily("Consolas"),
                    Margin = new Thickness(0, 2, 0, 2)
                });
                continue;
            }

            // Check for image markdown: ![alt](path)
            var imageMatch = ImageRegex.Match(trimmed);
            if (imageMatch.Success)
            {
                var alt = imageMatch.Groups["alt"].Value;
                var url = imageMatch.Groups["url"].Value;
                var textBefore = trimmed[..imageMatch.Index].Trim();
                var textAfter = trimmed[(imageMatch.Index + imageMatch.Length)..].Trim();

                // Add any text before image
                if (!string.IsNullOrEmpty(textBefore))
                {
                    var beforePara = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
                    AddRunsWithLinks(beforePara, textBefore);
                    doc.Blocks.Add(beforePara);
                }

                // Try to render as an actual image
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

                    // Wrap in a container with rounded corners and border
                    var border = new Border
                    {
                        CornerRadius = new CornerRadius(8),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(40, 99, 102, 241)),
                        BorderThickness = new Thickness(1),
                        Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)),
                        Padding = new Thickness(8),
                        Child = image
                    };

                    var container = new BlockUIContainer(border)
                    {
                        Margin = new Thickness(0, 4, 0, 4)
                    };
                    doc.Blocks.Add(container);
                }
                else
                {
                    // Fallback: show as text link
                    var fallback = new Paragraph(new Run($"[Image: {alt}] {url}"))
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(99, 102, 241)),
                        Margin = new Thickness(0, 0, 0, 4)
                    };
                    doc.Blocks.Add(fallback);
                }

                // Add any text after image
                if (!string.IsNullOrEmpty(textAfter))
                {
                    var afterPara = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
                    AddRunsWithLinks(afterPara, textAfter);
                    doc.Blocks.Add(afterPara);
                }
                continue;
            }

            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
            AddRunsWithLinks(paragraph, trimmed);
            doc.Blocks.Add(paragraph);
        }

        return doc;
    }

    private static void AddRunsWithLinks(Paragraph paragraph, string text)
    {
        var currentIndex = 0;
        foreach (Match match in LinkRegex.Matches(text))
        {
            if (match.Index > currentIndex)
            {
                paragraph.Inlines.Add(new Run(text[currentIndex..match.Index]));
            }

            var linkText = match.Groups["text"].Value;
            var linkUrl = match.Groups["url"].Value;
            if (Uri.TryCreate(linkUrl, UriKind.Absolute, out var uri))
            {
                var hyperlink = new Hyperlink(new Run(linkText))
                {
                    NavigateUri = uri
                };
                hyperlink.RequestNavigate += (_, _) =>
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = linkUrl,
                        UseShellExecute = true
                    });
                };
                paragraph.Inlines.Add(hyperlink);
            }
            else
            {
                paragraph.Inlines.Add(new Run(match.Value));
            }

            currentIndex = match.Index + match.Length;
        }

        if (currentIndex < text.Length)
        {
            paragraph.Inlines.Add(new Run(text[currentIndex..]));
        }
    }

    /// <summary>
    /// Try to load a local image file as a BitmapImage.
    /// Supports absolute file paths and common image formats.
    /// </summary>
    private static bool TryLoadImage(string path, out BitmapImage? image)
    {
        image = null;
        try
        {
            // Clean up the path
            var cleanPath = path.Trim().Trim('"', '\'');

            if (!File.Exists(cleanPath))
                return false;

            var ext = Path.GetExtension(cleanPath).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".ico" or ".webp" or ".tiff"))
                return false;

            image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(cleanPath, UriKind.Absolute);
            image.EndInit();
            image.Freeze(); // Thread-safe

            return true;
        }
        catch
        {
            image = null;
            return false;
        }
    }
}