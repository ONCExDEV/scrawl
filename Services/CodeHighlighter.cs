using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Scrawl.Services;

public enum Tok { Keyword, String, Number, Comment, Type, Func }

/// <summary>Small single-line tokenizer shared by the editor and the preview.</summary>
public static class CodeHighlighter
{
    private const string CLike = "abstract|as|async|await|base|bool|break|case|catch|char|class|const|continue|default|delegate|do|double|else|enum|export|extends|extern|false|final|finally|float|fn|for|foreach|from|func|function|get|go|if|implements|import|in|int|interface|internal|is|let|long|match|mod|namespace|new|nil|null|override|package|private|protected|pub|public|readonly|ref|return|self|set|static|string|struct|super|switch|this|throw|true|try|type|typeof|uint|unsafe|use|using|var|virtual|void|volatile|where|while|yield|record|impl|trait|mut|undefined|of|delete|with|init|val|when|object|println|fmt|defer|chan|select|range";
    private const string Python = "and|as|assert|async|await|break|class|continue|def|del|elif|else|except|finally|for|from|global|if|import|in|is|lambda|None|nonlocal|not|or|pass|raise|return|self|True|False|try|while|with|yield|print";
    private const string Shell = "if|then|else|elif|fi|for|while|do|done|case|esac|function|in|return|export|local|echo|cd|ls|set|unset|alias|exit|sudo|git|npm|dotnet|pip|param|foreach|Write-Host|Get-ChildItem|Set-Location|true|false";
    private const string Sql = "(?i:select|from|where|insert|into|values|update|set|delete|create|table|alter|drop|join|left|right|inner|outer|on|group|by|order|having|limit|as|and|or|not|null|is|in|like|distinct|union|index|primary|key|foreign|references|default|case|when|then|else|end)";

    private static readonly ConcurrentDictionary<string, Regex> Cache = new();

    public static readonly (string Id, string Name)[] Languages =
    [
        ("text", "Plain text"), ("csharp", "C#"), ("javascript", "JavaScript"), ("typescript", "TypeScript"),
        ("python", "Python"), ("java", "Java"), ("cpp", "C / C++"), ("go", "Go"), ("rust", "Rust"), ("json", "JSON"),
        ("html", "HTML / XML"), ("css", "CSS"), ("sql", "SQL"), ("shell", "Shell / PowerShell")
    ];

    public static string NameOf(string id) => Languages.FirstOrDefault(l => l.Id == id).Name ?? id;

    private static readonly (string Id, Regex Pattern)[] Guesses =
    [
        ("json", new(@"\A\s*[\[{][\s\S]*""\s*:", RegexOptions.Compiled)),
        ("csharp", new(@"^\s*(using\s+System|namespace\s+\w|(public|internal|private)\s+(static\s+|sealed\s+|abstract\s+)*(class|struct|record|interface)\s|Console\.Write)", RegexOptions.Multiline | RegexOptions.Compiled)),
        ("java", new(@"public\s+static\s+void\s+main|System\.out\.print", RegexOptions.Compiled)),
        ("cpp", new(@"^\s*#include\s*[<""]|std::", RegexOptions.Multiline | RegexOptions.Compiled)),
        ("go", new(@"^package\s+\w+|fmt\.Print|^func\s+\w+\(", RegexOptions.Multiline | RegexOptions.Compiled)),
        ("rust", new(@"fn\s+\w+\s*\(|let\s+mut\s|println!\(", RegexOptions.Compiled)),
        ("python", new(@"^\s*(def\s+\w+\(|from\s+\w+\s+import|import\s+\w+\s*$|class\s+\w+(\(.*\))?:|print\()", RegexOptions.Multiline | RegexOptions.Compiled)),
        ("html", new(@"<(!DOCTYPE|html|head|body|div|span|script|style)", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("sql", new(@"^\s*(SELECT\s.+\sFROM|INSERT\s+INTO|UPDATE\s+\w+\s+SET|CREATE\s+TABLE|DELETE\s+FROM)", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled)),
        ("javascript", new(@"^\s*(const|let|var)\s+\w+\s*=|^\s*function\s+\w+\(|=>\s*[{(]|console\.log\(|^\s*import\s.+\sfrom\s|require\(", RegexOptions.Multiline | RegexOptions.Compiled)),
        ("css", new(@"^[\w.#:\s,>-]+\{[^}]*[\w-]+\s*:\s*[^;]+;", RegexOptions.Multiline | RegexOptions.Compiled)),
        ("shell", new(@"^#!/bin/|^\s*(sudo|echo|cd|npm|git|dotnet|Write-Host|Get-ChildItem)\s", RegexOptions.Multiline | RegexOptions.Compiled)),
    ];

    /// <summary>Best-effort language guess; null when the text doesn't clearly look like code.</summary>
    public static string? Guess(string text)
    {
        foreach (var (id, pattern) in Guesses)
            if (pattern.IsMatch(text)) return id;
        return null;
    }

    public static IEnumerable<(int Start, int Length, Tok Kind)> Tokenize(string line, string language)
    {
        if (line.Length == 0 || string.IsNullOrWhiteSpace(language) || language == "text") yield break;
        var regex = Cache.GetOrAdd(Canonical(language), Build);
        foreach (Match m in regex.Matches(line))
        {
            if (m.Groups["com"].Success) yield return (m.Index, m.Length, Tok.Comment);
            else if (m.Groups["str"].Success) yield return (m.Index, m.Length, Tok.String);
            else if (m.Groups["num"].Success) yield return (m.Index, m.Length, Tok.Number);
            else if (m.Groups["kw"].Success) yield return (m.Index, m.Length, Tok.Keyword);
            else if (m.Groups["fn"].Success) yield return (m.Index, m.Length, Tok.Func);
            else if (m.Groups["ty"].Success) yield return (m.Index, m.Length, Tok.Type);
        }
    }

    private static string Canonical(string language) => language.Trim().ToLowerInvariant() switch
    {
        "py" or "python" => "py",
        "sh" or "bash" or "zsh" or "shell" or "powershell" or "ps1" or "pwsh" or "bat" or "cmd" or "yaml" or "yml" or "toml" or "ini" or "dockerfile" => "sh",
        "sql" => "sql",
        "json" or "jsonc" => "json",
        "html" or "xml" or "xaml" or "svg" or "csproj" => "html",
        "css" or "scss" or "less" => "css",
        _ => "c"
    };

    private static Regex Build(string kind)
    {
        const string str = "\"(?:\\\\.|[^\"\\\\])*\"?|'(?:\\\\.|[^'\\\\])*'?|`(?:\\\\.|[^`\\\\])*`?";
        const string num = @"\b0x[0-9A-Fa-f]+\b|\b\d+(?:\.\d+)?\b";
        var pattern = kind switch
        {
            "py" => $@"(?<com>#.*)|(?<str>{str})|(?<num>{num})|(?<kw>\b(?:{Python})\b)|(?<fn>\b[A-Za-z_]\w*(?=\s*\())|(?<ty>\b[A-Z]\w*\b)",
            "sh" => $@"(?<com>(?:^|\s)#.*)|(?<str>{str})|(?<num>{num})|(?<kw>\b(?:{Shell})\b)|(?<ty>\$\{{?\w+\}}?|--?[A-Za-z][\w-]*)",
            "sql" => $@"(?<com>--.*|/\*.*?\*/)|(?<str>'(?:''|[^'])*'?)|(?<num>{num})|(?<kw>\b{Sql}\b)|(?<fn>\b[A-Za-z_]\w*(?=\s*\())",
            "json" => $@"(?<ty>""(?:\\.|[^""\\])*""(?=\s*:))|(?<str>""(?:\\.|[^""\\])*""?)|(?<num>{num})|(?<kw>\b(?:true|false|null)\b)",
            "html" => $@"(?<com><!--.*?-->)|(?<str>{str})|(?<kw></?[A-Za-z][\w:.-]*|/?>)|(?<ty>\b[\w:-]+(?==))",
            "css" => $@"(?<com>/\*.*?\*/)|(?<str>{str})|(?<num>#[0-9A-Fa-f]{{3,8}}\b|\b\d+(?:\.\d+)?(?:px|em|rem|%|vh|vw|s|ms|deg)?\b)|(?<kw>@[\w-]+)|(?<ty>[\w-]+(?=\s*:))|(?<fn>\b[a-z-]+(?=\())",
            _ => $@"(?<com>//.*|/\*.*?\*/|^\s*#(?!\w+\s*=).*)|(?<str>{str})|(?<num>{num})|(?<kw>\b(?:{CLike})\b)|(?<fn>\b[A-Za-z_]\w*(?=\s*\())|(?<ty>\b[A-Z][A-Za-z0-9_]*\b)"
        };
        return new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }
}
