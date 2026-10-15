using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Indentation;
using ICSharpCode.AvalonEdit.Rendering;

namespace Scrawl.Services;

/// <summary>Colors a whole document with the theme's syntax colors. "text" means no coloring at all.</summary>
public sealed class CodeColorizer : DocumentColorizingTransformer
{
    public string Language { get; set; } = "text";

    protected override void ColorizeLine(DocumentLine line)
    {
        if (Language == "text" || line.Length == 0) return;
        var text = CurrentContext.Document.GetText(line);
        foreach (var (start, length, kind) in CodeHighlighter.Tokenize(text, Language))
        {
            var brush = Themes.Get(kind.ToString());
            ChangeLinePart(line.Offset + start, line.Offset + start + length, element => element.TextRunProperties.SetForegroundBrush(brush));
        }
    }
}

/// <summary>Notepad behaviour: Enter never copies the previous line's indentation.</summary>
public sealed class NoIndentation : IIndentationStrategy
{
    public void IndentLine(TextDocument document, DocumentLine line) { }
    public void IndentLines(TextDocument document, int beginLine, int endLine) { }
}
