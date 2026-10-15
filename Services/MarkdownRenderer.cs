using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Morrow.Services;

public static class MarkdownRenderer
{
    private static readonly Brush Text = Brush("#41424A");
    private static readonly Brush Muted = Brush("#9798A2");
    private static readonly Brush Purple = Brush("#7768E8");
    private static readonly Brush Green = Brush("#448B69");
    private static readonly Brush Blue = Brush("#3478C5");
    private static readonly Brush Orange = Brush("#C47B35");
    private static readonly Brush CodeBackground = Brush("#F5F4F8");
    private static readonly Brush Border = Brush("#EAE9EE");
    private static readonly Regex TokenRegex = new(@"(""(?:\\.|[^""])*""|'(?:\\.|[^'])*'|//.*|#.*|\b(?:const|let|var|function|return|if|else|for|while|class|new|public|private|static|void|int|string|bool|using|namespace|async|await|export|import|from|def|True|False|null|true|false|None)\b|\b\d+(?:\.\d+)?\b)", RegexOptions.Compiled);
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));

    public static void Render(Panel host, string markdown)
    {
        host.Children.Clear();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                var language = line.Trim()[3..].Trim();
                var codeLines = new List<string>();
                while (++i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal)) codeLines.Add(lines[i]);
                AddCode(host, codeLines, language);
                continue;
            }

            if (line.StartsWith("    ", StringComparison.Ordinal) || line.StartsWith('\t'))
            {
                var codeLines = new List<string>();
                do
                {
                    codeLines.Add(lines[i].StartsWith('\t') ? lines[i][1..] : lines[i].Length >= 4 ? lines[i][4..] : lines[i]);
                    i++;
                }
                while (i < lines.Length && (lines[i].StartsWith("    ", StringComparison.Ordinal) || lines[i].StartsWith('\t')));
                i--;
                AddCode(host, codeLines, string.Empty);
                continue;
            }

            if (IsStrongCode(line))
            {
                var codeLines = new List<string> { line };
                while (i + 1 < lines.Length && !string.IsNullOrWhiteSpace(lines[i + 1]) && IsCodeLike(lines[i + 1]))
                    codeLines.Add(lines[++i]);
                AddCode(host, codeLines, GuessLanguage(codeLines));
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                host.Children.Add(new Border { Height = 9 });
                continue;
            }

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("#"))
            {
                var level = Math.Min(6, trimmed.TakeWhile(c => c == '#').Count());
                var content = trimmed[level..].TrimStart();
                AddRichLine(host, content, level switch { 1 => 30d, 2 => 23d, 3 => 19d, _ => 16d }, FontWeights.SemiBold, 7);
            }
            else if (Regex.IsMatch(trimmed, @"^[-*+]\s+"))
            {
                var bullet = new TextBlock { FontFamily = new FontFamily("Segoe UI Variable Text"), FontSize = 15, Foreground = Purple, Margin = new Thickness(0, 1, 9, 0), Text = "•" };
                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                DockPanel.SetDock(bullet, Dock.Left);
                row.Children.Add(bullet);
                var body = Inline(trimmed[2..]);
                body.FontSize = 15;
                row.Children.Add(body);
                host.Children.Add(row);
            }
            else if (Regex.IsMatch(trimmed, @"^\d+[.)]\s+"))
            {
                var match = Regex.Match(trimmed, @"^(\d+[.)])\s+(.*)$");
                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                var marker = new TextBlock { Text = match.Groups[1].Value, Foreground = Purple, FontSize = 14, Margin = new Thickness(0, 0, 9, 0) };
                DockPanel.SetDock(marker, Dock.Left); row.Children.Add(marker);
                var body = Inline(match.Groups[2].Value); body.FontSize = 15; row.Children.Add(body); host.Children.Add(row);
            }
            else
            {
                AddRichLine(host, trimmed, 15, FontWeights.Normal, 1);
            }
        }
    }

    private static void AddRichLine(Panel host, string content, double size, FontWeight weight, double bottom)
    {
        var block = Inline(content);
        block.FontSize = size; block.FontWeight = weight; block.Margin = new Thickness(0, 0, 0, bottom);
        host.Children.Add(block);
    }

    private static TextBlock Inline(string source)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Text, LineHeight = 24, FontFamily = new FontFamily("Segoe UI Variable Text") };
        var matches = Regex.Matches(source, @"(`[^`]+`|\*\*.+?\*\*|\*.+?\*|\[([^\]]+)\]\([^)]+\))");
        var position = 0;
        foreach (Match match in matches)
        {
            if (match.Index > position) block.Inlines.Add(new Run(source[position..match.Index]));
            var value = match.Value;
            if (value.StartsWith('`')) block.Inlines.Add(new Run(value[1..^1]) { FontFamily = new FontFamily("Cascadia Code"), Foreground = Purple, Background = CodeBackground });
            else if (value.StartsWith("**")) block.Inlines.Add(new Run(value[2..^2]) { FontWeight = FontWeights.SemiBold, Foreground = Brush("#24232C") });
            else if (value.StartsWith('*')) block.Inlines.Add(new Run(value[1..^1]) { FontStyle = FontStyles.Italic });
            else block.Inlines.Add(new Run(match.Groups[2].Value) { Foreground = Blue, TextDecorations = TextDecorations.Underline });
            position = match.Index + match.Length;
        }
        if (position < source.Length) block.Inlines.Add(new Run(source[position..]));
        if (source.Length == 0) block.Inlines.Add(new Run(" "));
        return block;
    }

    private static void AddCode(Panel host, List<string> lines, string language)
    {
        if (lines.Count == 0) lines.Add(" ");
        if (string.IsNullOrWhiteSpace(language) && lines.Any(IsCodeLike)) language = GuessLanguage(lines);
        var container = new Border { Background = CodeBackground, BorderBrush = Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 5, 0, 12) };
        var stack = new StackPanel();
        if (!string.IsNullOrWhiteSpace(language)) stack.Children.Add(new TextBlock { Text = language.ToUpperInvariant(), FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = Muted, Margin = new Thickness(0, 0, 0, 8) });
        for (var i = 0; i < lines.Count; i++)
        {
            var row = new DockPanel { LastChildFill = true };
            var number = new TextBlock { Text = (i + 1).ToString(), Width = 28, Foreground = Muted, FontFamily = new FontFamily("Cascadia Code"), FontSize = 12, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 0, 14, 0) };
            DockPanel.SetDock(number, Dock.Left); row.Children.Add(number);
            var code = new TextBlock { FontFamily = new FontFamily("Cascadia Code"), FontSize = 12.5, Foreground = Brush("#454550"), Text = lines[i] };
            AddTokens(code, lines[i]);
            row.Children.Add(code); stack.Children.Add(row);
        }
        container.Child = stack;
        host.Children.Add(container);
    }

    private static void AddTokens(TextBlock block, string line)
    {
        block.Inlines.Clear();
        var matches = TokenRegex.Matches(line);
        var position = 0;
        foreach (Match match in matches)
        {
            if (match.Index > position) block.Inlines.Add(new Run(line[position..match.Index]));
            var value = match.Value;
            var color = value.StartsWith('"') || value.StartsWith('\'') ? Green : value.StartsWith("//") || value.StartsWith('#') ? Muted : char.IsDigit(value[0]) ? Orange : char.IsLetter(value[0]) && char.IsUpper(value[0]) ? Blue : Purple;
            block.Inlines.Add(new Run(value) { Foreground = color });
            position = match.Index + match.Length;
        }
        if (position < line.Length) block.Inlines.Add(new Run(line[position..]));
        if (line.Length == 0) block.Inlines.Add(new Run(" "));
    }

    private static bool IsCodeLike(string line) => IsStrongCode(line) || Regex.IsMatch(line, @"^\s*[{}]\s*$|^\s*(?:else|try|catch|finally)\b");

    private static bool IsStrongCode(string line) => Regex.IsMatch(line,
        @"^\s*(?:var|const|let|function|class|def|return|using|namespace|public|private|import|export)\s+[\w$]|^\s*#include\b|(?:=>|===?|==|!=|&&|\|\||[{};])|^\s*[\w$.]+\s*\([^)]*\)\s*;?\s*$|^\s*[\w$.]+\s*(?<![=])=(?!=)\s*.+;?\s*$");

    private static string GuessLanguage(IEnumerable<string> lines)
    {
        var sample = string.Join(" ", lines);
        if (Regex.IsMatch(sample, @"\b(Console\.WriteLine|using|namespace|string|public|private)\b")) return "csharp";
        if (Regex.IsMatch(sample, @"\b(const|let|function|console\.log)\b")) return "javascript";
        if (Regex.IsMatch(sample, @"\b(def|print)\b")) return "python";
        if (Regex.IsMatch(sample, @"<\w+[^>]*>")) return "html";
        return "code";
    }
}
