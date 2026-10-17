using System.Globalization;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Win32;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.DirectWrite;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.System.Com;

namespace Scrawl.Services;

/// <summary>A color emoji drawn to a bitmap. Sizes are in WPF units; <see cref="Baseline"/> is the distance from the top down to the text baseline.</summary>
public sealed record EmojiImage(BitmapSource Source, double Width, double Height, double Baseline);

/// <summary>
/// WPF draws emoji in one color only. This lets Windows' own DirectWrite draw them with their colors (Segoe UI Emoji),
/// one emoji at a time, and finds where the emoji in a text are (including skin tones, families, keycaps and flags).
/// If DirectWrite is not available nothing breaks: text just shows the plain way.
/// </summary>
public static unsafe class ColorEmoji
{
    private sealed class Factories
    {
        public required ID2D1Factory Direct2D { get; init; }
        public required IDWriteFactory DirectWrite { get; init; }
        public required IWICImagingFactory Imaging { get; init; }
    }

    private static Factories? _factories;
    private static bool _unavailable;
    private static readonly Dictionary<(string Text, int Size, int Dpi, uint Color), EmojiImage?> Cache = new();

    private static Factories? GetFactories()
    {
        if (_factories is not null || _unavailable) return _factories;
        try
        {
            var d2dId = typeof(ID2D1Factory).GUID;
            PInvoke.D2D1CreateFactory(D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_SINGLE_THREADED, &d2dId, null, out object d2d).ThrowOnFailure();
            var dwId = typeof(IDWriteFactory).GUID;
            PInvoke.DWriteCreateFactory(DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED, &dwId, out object dw).ThrowOnFailure();
            var wicClass = PInvoke.CLSID_WICImagingFactory;
            var wicId = typeof(IWICImagingFactory).GUID;
            PInvoke.CoCreateInstance(&wicClass, null, CLSCTX.CLSCTX_INPROC_SERVER, &wicId, out object wic).ThrowOnFailure();
            _factories = new Factories { Direct2D = (ID2D1Factory)d2d, DirectWrite = (IDWriteFactory)dw, Imaging = (IWICImagingFactory)wic };
        }
        catch (Exception)
        {
            _unavailable = true;
        }
        return _factories;
    }

    public static bool IsAvailable => GetFactories() is not null;

    // ───────────── Finding emoji in text ─────────────

    /// <summary>Emoji that Windows shows in color without a variation selector after them (the rest of the symbols below U+2B5F need U+FE0F).</summary>
    private static readonly HashSet<int> ColorByDefault =
    [
        0x231A, 0x231B, 0x23E9, 0x23EA, 0x23EB, 0x23EC, 0x23F0, 0x23F3, 0x25FD, 0x25FE, 0x2614, 0x2615, 0x267F, 0x2693, 0x26A1, 0x26AA, 0x26AB, 0x26BD, 0x26BE,
        0x26C4, 0x26C5, 0x26CE, 0x26D4, 0x26EA, 0x26F2, 0x26F3, 0x26F5, 0x26FA, 0x26FD, 0x2705, 0x270A, 0x270B, 0x2728, 0x274C, 0x274E, 0x2753, 0x2754, 0x2755,
        0x2757, 0x2795, 0x2796, 0x2797, 0x27B0, 0x27BF, 0x2B1B, 0x2B1C, 0x2B50, 0x2B55,
    ];

    private static bool InRange(int value, int low, int high) => value >= low && value <= high;

    /// <summary>True when a text element (one "character" as the user sees it) is an emoji.</summary>
    public static bool IsEmoji(string cluster)
    {
        if (cluster.Length == 0) return false;
        if (cluster.Contains('️') || cluster.Contains('⃣')) return true;
        if (!Rune.TryGetRuneAt(cluster, 0, out var rune)) return false;   // a lone half of a surrogate pair (for example a cut-off emoji) is no emoji
        var first = rune.Value;
        if (InRange(first, 0x1F1E6, 0x1F1FF)) return cluster.Length >= 4;   // a flag is two regional indicators
        if (InRange(first, 0x1F000, 0x1FAFF)) return true;
        if (InRange(first, 0x2000, 0x2BFF)) return ColorByDefault.Contains(first);
        return false;
    }

    /// <summary>Finds the first emoji in <paramref name="text"/> at or after <paramref name="start"/>.</summary>
    public static (int Index, int Length)? Find(string text, int start = 0)
    {
        if (start >= text.Length) return null;
        // Nothing before U+2000 can be an emoji, except the digits, # and * of keycaps (which carry U+20E3).
        if (text.AsSpan(start).IndexOfAnyExceptInRange('\0', '῿') < 0) return null;
        // Work on the rest of the text only, so the positions found are plain offsets from its start.
        var rest = text[start..];
        var elements = StringInfo.GetTextElementEnumerator(rest);
        while (elements.MoveNext())
        {
            var cluster = (string)elements.Current;
            if (IsEmoji(cluster)) return (start + elements.ElementIndex, cluster.Length);
        }
        return null;
    }

    /// <summary>True when the text contains at least one emoji.</summary>
    public static bool Contains(string text) => Find(text) is not null;

    // ───────────── Drawing ─────────────

    /// <summary>
    /// Draws one emoji for text of this font size (WPF units) on a screen with this scale (1 = 96 dpi). Null when it cannot be drawn.
    /// Real emoji keep their own colors; <paramref name="textColor"/> is for the symbols Windows only has as plain text (flags show their letters).
    /// </summary>
    public static EmojiImage? Render(string emoji, double fontSize, double dpiScale, Color textColor)
    {
        var key = (emoji, (int)Math.Round(fontSize * 10), (int)Math.Round(dpiScale * 100), (uint)(textColor.R << 16 | textColor.G << 8 | textColor.B));
        if (Cache.TryGetValue(key, out var cached)) return cached;
        EmojiImage? image;
        try { image = Draw(emoji, fontSize, dpiScale, textColor); }
        catch (Exception) { image = null; }
        if (Cache.Count > 4000) Cache.Clear();
        Cache[key] = image;
        return image;
    }

    private static EmojiImage? Draw(string emoji, double fontSize, double dpiScale, Color textColor)
    {
        if (GetFactories() is not { } f) return null;
        f.DirectWrite.CreateTextFormat("Segoe UI Emoji", null, DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_REGULAR, DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL,
            DWRITE_FONT_STRETCH.DWRITE_FONT_STRETCH_NORMAL, (float)fontSize, "en-us", out var format);
        f.DirectWrite.CreateTextLayout(emoji, (uint)emoji.Length, format, 1000, 1000, out var layout);
        layout.GetMetrics(out var metrics);
        DWRITE_LINE_METRICS line = default;
        layout.GetLineMetrics(&line, 1, out _);

        var width = Math.Max(1, Math.Ceiling(metrics.widthIncludingTrailingWhitespace));
        var height = Math.Max(1, Math.Ceiling(metrics.height));
        var pixelWidth = (int)Math.Ceiling(width * dpiScale);
        var pixelHeight = (int)Math.Ceiling(height * dpiScale);
        var pixelFormat = PInvoke.GUID_WICPixelFormat32bppPBGRA;
        f.Imaging.CreateBitmap((uint)pixelWidth, (uint)pixelHeight, &pixelFormat, WICBitmapCreateCacheOption.WICBitmapCacheOnLoad, out var bitmap);

        var properties = new D2D1_RENDER_TARGET_PROPERTIES
        {
            type = D2D1_RENDER_TARGET_TYPE.D2D1_RENDER_TARGET_TYPE_DEFAULT,
            pixelFormat = new D2D1_PIXEL_FORMAT { format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED },
            dpiX = (float)(96 * dpiScale),
            dpiY = (float)(96 * dpiScale),
        };
        f.Direct2D.CreateWicBitmapRenderTarget(bitmap, in properties, out var target);
        var ink = new D2D1_COLOR_F { r = textColor.R / 255f, g = textColor.G / 255f, b = textColor.B / 255f, a = 1 };
        target.CreateSolidColorBrush(in ink, null, out var brush);
        var clear = new D2D1_COLOR_F { r = 0, g = 0, b = 0, a = 0 };
        target.BeginDraw();
        target.Clear(&clear);
        target.DrawTextLayout(new D2D_POINT_2F { x = 0, y = 0 }, layout, brush, D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT);
        target.EndDraw();

        var rect = new WICRect { X = 0, Y = 0, Width = pixelWidth, Height = pixelHeight };
        bitmap.Lock(in rect, (uint)WICBitmapLockFlags.WICBitmapLockRead, out var locked);
        byte* data = null;
        locked.GetDataPointer(out var size, &data);
        locked.GetStride(out var stride);
        var pixels = new byte[size];
        System.Runtime.InteropServices.Marshal.Copy((nint)data, pixels, 0, (int)size);
        var source = BitmapSource.Create(pixelWidth, pixelHeight, 96 * dpiScale, 96 * dpiScale, PixelFormats.Pbgra32, null, pixels, (int)stride);
        source.Freeze();
        return new EmojiImage(source, width, height, line.baseline);
    }
}
