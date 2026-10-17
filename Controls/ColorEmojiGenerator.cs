using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using Scrawl.Services;

namespace Scrawl.Controls;

/// <summary>Makes the editor show emoji in color: every emoji in the text is replaced, on screen only, by a picture of it.</summary>
public sealed class ColorEmojiGenerator : VisualLineElementGenerator
{
    private readonly Func<double> _fontSize;
    private readonly Func<Color> _textColor;

    public ColorEmojiGenerator(Func<double> fontSize, Func<Color> textColor)
    {
        _fontSize = fontSize;
        _textColor = textColor;
    }

    private string RestOfLine(int offset)
    {
        var end = CurrentContext.VisualLine.LastDocumentLine.EndOffset;
        return end > offset ? CurrentContext.Document.GetText(offset, end - offset) : string.Empty;
    }

    public override int GetFirstInterestedOffset(int startOffset) =>
        ColorEmoji.IsAvailable && ColorEmoji.Find(RestOfLine(startOffset)) is { } found ? startOffset + found.Index : -1;

    public override VisualLineElement? ConstructElement(int offset)
    {
        var text = RestOfLine(offset);
        if (ColorEmoji.Find(text) is not { Index: 0, Length: var length }) return null;
        var scale = VisualTreeHelper.GetDpi(CurrentContext.TextView).DpiScaleX;
        if (ColorEmoji.Render(text[..length], _fontSize(), scale, _textColor()) is not { } emoji) return null;
        var picture = new Image { Source = emoji.Source, Width = emoji.Width, Height = emoji.Height, Stretch = Stretch.Fill, SnapsToDevicePixels = true };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        TextBlock.SetBaselineOffset(picture, emoji.Baseline);
        return new InlineObjectElement(length, picture);
    }
}
