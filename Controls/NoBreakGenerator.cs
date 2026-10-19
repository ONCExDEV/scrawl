using System.Windows;
using System.Windows.Documents;
using ICSharpCode.AvalonEdit.Document;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using ICSharpCode.AvalonEdit.Rendering;

namespace Scrawl.Controls;

/// <summary>
/// Wraps long lines like Windows Notepad does: a long web address or code line fills the whole line and breaks at the edge,
/// instead of jumping to the next line at the first "/" or "." it can (which leaves big gaps on the right).
/// Between two characters where one is punctuation (like "/", "(" or a quote) it puts an invisible "do not break here" marker,
/// so only spaces (or the edge of the window) end a line.
/// The note's text is not changed; the marker exists on screen only.
/// </summary>
public sealed class NoBreakGenerator : VisualLineElementGenerator
{
    private readonly Func<bool> _enabled;

    public NoBreakGenerator(Func<bool> enabled) => _enabled = enabled;

    private static bool IsSign(char c) => char.IsPunctuation(c) || char.IsSymbol(c);

    private static bool WantsMarker(char before, char after) =>
        !char.IsWhiteSpace(before) && !char.IsWhiteSpace(after) && (IsSign(before) || IsSign(after));

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!_enabled()) return -1;
        var document = CurrentContext.Document;
        var end = CurrentContext.VisualLine.LastDocumentLine.EndOffset;
        for (var i = Math.Max(startOffset, 1); i < end; i++)
            if (WantsMarker(document.GetCharAt(i - 1), document.GetCharAt(i))) return i;
        return -1;
    }

    public override VisualLineElement ConstructElement(int offset) => new NoBreakElement();

    private sealed class NoBreakElement : VisualLineElement
    {
        public NoBreakElement() : base(1, 0) { }

        public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context) => new NoBreakRun(TextRunProperties);

        // The marker has no width, so it must not cost an arrow key press: the caret steps over it, to the next or previous character.
        public override int GetNextCaretPosition(int visualColumn, LogicalDirection direction, CaretPositioningMode mode)
        {
            if (mode != CaretPositioningMode.Normal) return -1;
            if (direction == LogicalDirection.Forward) return visualColumn <= VisualColumn ? VisualColumn + 2 : -1;
            return visualColumn > VisualColumn && visualColumn <= VisualColumn + 1 ? VisualColumn - 1 : -1;
        }

        public override bool IsWhitespace(int visualColumn) => false;
    }

    private sealed class NoBreakRun : TextEmbeddedObject
    {
        private readonly TextRunProperties _properties;

        public NoBreakRun(TextRunProperties properties) => _properties = properties;

        public override LineBreakCondition BreakBefore => LineBreakCondition.BreakRestrained;
        public override LineBreakCondition BreakAfter => LineBreakCondition.BreakRestrained;
        public override bool HasFixedSize => true;
        public override CharacterBufferReference CharacterBufferReference => new(" ", 0);
        public override int Length => 1;
        public override TextRunProperties Properties => _properties;

        public override TextEmbeddedObjectMetrics Format(double remainingParagraphWidth) => new(0, 0, 0);
        public override Rect ComputeBoundingBox(bool rightToLeft, bool sideways) => Rect.Empty;
        public override void Draw(DrawingContext drawingContext, Point origin, bool rightToLeft, bool sideways) { }
    }
}
