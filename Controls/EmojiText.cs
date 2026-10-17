using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Scrawl.Services;

namespace Scrawl.Controls;

/// <summary>
/// Text for a TextBlock that shows emoji in color: <c>controls:EmojiText.Text="{Binding Title}"</c> or <see cref="SetText"/>.
/// Text without emoji is set the normal way.
/// </summary>
public static class EmojiText
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string), typeof(EmojiText), new PropertyMetadata(string.Empty, (d, e) => Fill((TextBlock)d, (string)e.NewValue)));

    public static string GetText(DependencyObject d) => (string)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string value) => d.SetValue(TextProperty, value);

    private static void Fill(TextBlock block, string text)
    {
        text ??= string.Empty;
        if (!ColorEmoji.IsAvailable || !ColorEmoji.Contains(text))
        {
            block.Text = text;
            return;
        }
        block.Inlines.Clear();
        var scale = VisualTreeHelper.GetDpi(block).DpiScaleX;
        var color = (block.Foreground as SolidColorBrush)?.Color ?? Colors.White;
        var position = 0;
        while (ColorEmoji.Find(text, position) is { } found)
        {
            if (found.Index > position) block.Inlines.Add(new Run(text[position..found.Index]));
            var emoji = text.Substring(found.Index, found.Length);
            if (ColorEmoji.Render(emoji, block.FontSize, scale, color) is { } image)
            {
                var picture = new Image { Source = image.Source, Width = image.Width, Height = image.Height, Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
                TextBlock.SetBaselineOffset(picture, image.Baseline);
                block.Inlines.Add(new InlineUIContainer(picture) { BaselineAlignment = BaselineAlignment.Baseline });
            }
            else
            {
                block.Inlines.Add(new Run(emoji));
            }
            position = found.Index + found.Length;
        }
        if (position < text.Length) block.Inlines.Add(new Run(text[position..]));
    }
}
