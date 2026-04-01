using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ZayFlow.App.Controls;

public class CodeArtifactViewer : RichTextBox
{
    public static readonly DependencyProperty CodeTextProperty = DependencyProperty.Register(
        nameof(CodeText),
        typeof(string),
        typeof(CodeArtifactViewer),
        new PropertyMetadata(string.Empty, OnCodeChanged));

    public static readonly DependencyProperty LanguageProperty = DependencyProperty.Register(
        nameof(Language),
        typeof(string),
        typeof(CodeArtifactViewer),
        new PropertyMetadata("text", OnCodeChanged));

    private static readonly Brush KeywordBrush = new SolidColorBrush(Color.FromRgb(129, 140, 248));
    private static readonly Brush AddedBrush = new SolidColorBrush(Color.FromRgb(52, 211, 153));
    private static readonly Brush RemovedBrush = new SolidColorBrush(Color.FromRgb(248, 113, 113));
    private static readonly Brush LineNumberBrush = new SolidColorBrush(Color.FromRgb(124, 124, 142));
    private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(180, 180, 198));
    private static readonly FontFamily MonoFont = new("Cascadia Code, Consolas, Courier New");

    public string CodeText
    {
        get => (string)GetValue(CodeTextProperty);
        set => SetValue(CodeTextProperty, value);
    }

    public string Language
    {
        get => (string)GetValue(LanguageProperty);
        set => SetValue(LanguageProperty, value);
    }

    public CodeArtifactViewer()
    {
        IsReadOnly = true;
        IsDocumentEnabled = false;
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        FontFamily = MonoFont;
        FontSize = 12.5;
        Padding = new Thickness(8, 6, 8, 6);
        SetResourceReference(ForegroundProperty, "PrimaryTextBrush");
    }

    private static void OnCodeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not CodeArtifactViewer viewer)
        {
            return;
        }

        var doc = viewer.BuildDocument();
        doc.SetResourceReference(FlowDocument.ForegroundProperty, "PrimaryTextBrush");
        viewer.Document = doc;
    }

    private FlowDocument BuildDocument()
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = MonoFont,
            FontSize = 12.5,
            Background = Brushes.Transparent
        };

        var lines = (CodeText ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = 18 };
            paragraph.Inlines.Add(new Run($"{i + 1,4}  ") { Foreground = LineNumberBrush });

            var line = lines[i];
            if (Language.Equals("diff", StringComparison.OrdinalIgnoreCase))
            {
                var brush = line.StartsWith("+", StringComparison.Ordinal) ? AddedBrush
                    : line.StartsWith("-", StringComparison.Ordinal) ? RemovedBrush
                    : MutedBrush;
                paragraph.Inlines.Add(new Run(line) { Foreground = brush });
                document.Blocks.Add(paragraph);
                continue;
            }

            AppendHighlightedLine(paragraph.Inlines, line, Language);
            document.Blocks.Add(paragraph);
        }

        return document;
    }

    private static void AppendHighlightedLine(InlineCollection inlines, string line, string language)
    {
        var keywords = language.ToLowerInvariant() switch
        {
            "csharp" => new[] { "public", "private", "internal", "class", "using", "return", "async", "await", "if", "else", "new", "var", "string", "bool", "int", "Task" },
            "python" => new[] { "def", "class", "import", "from", "return", "if", "elif", "else", "for", "while", "try", "except", "with" },
            "javascript" or "typescript" => new[] { "function", "const", "let", "var", "return", "if", "else", "class", "import", "export", "async", "await" },
            "json" => Array.Empty<string>(),
            _ => new[] { "class", "return", "if", "else", "for", "while" }
        };

        if (keywords.Length == 0)
        {
            inlines.Add(new Run(line));
            return;
        }

        var pattern = $@"\b({string.Join("|", keywords.Select(Regex.Escape))})\b";
        var lastIndex = 0;
        foreach (Match match in Regex.Matches(line, pattern))
        {
            if (match.Index > lastIndex)
            {
                inlines.Add(new Run(line[lastIndex..match.Index]));
            }

            inlines.Add(new Run(match.Value) { Foreground = KeywordBrush, FontWeight = FontWeights.SemiBold });
            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < line.Length)
        {
            inlines.Add(new Run(line[lastIndex..]));
        }
    }
}
