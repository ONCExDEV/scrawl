using System.Windows;
using System.Windows.Media;

namespace Scrawl.Services;

public sealed record Theme(string Name, bool IsDark, string[] Colors);

public static class Themes
{
    // Order matches Keys.
    public static readonly string[] Keys =
    [
        "Bg", "Side", "Text", "Muted", "Line", "Accent", "AccentText", "Sel", "CurLine", "Hover", "Select", "CodeBg",
        "Keyword", "String", "Number", "Comment", "Type", "Func", "Heading", "Link"
    ];

    public static readonly Theme[] All =
    [
        new("Dark+", true, ["#1E1E1E", "#252526", "#D4D4D4", "#858585", "#333333", "#007ACC", "#FFFFFF", "#264F78", "#2A2D2E", "#2A2D2E", "#37373D", "#252526",
            "#569CD6", "#CE9178", "#B5CEA8", "#6A9955", "#4EC9B0", "#DCDCAA", "#569CD6", "#3794FF"]),
        new("Light+", false, ["#FFFFFF", "#F3F3F3", "#333333", "#8A8A8A", "#E5E5E5", "#007ACC", "#FFFFFF", "#ADD6FF", "#F5F5F5", "#E8E8E8", "#E4E6F1", "#F3F3F3",
            "#0000FF", "#A31515", "#098658", "#008000", "#267F99", "#795E26", "#800000", "#0066CC"]),
        new("Monokai", true, ["#272822", "#1E1F1C", "#F8F8F2", "#75715E", "#3E3D32", "#A6E22E", "#272822", "#49483E", "#34352C", "#34352C", "#3E3D32", "#1E1F1C",
            "#F92672", "#E6DB74", "#AE81FF", "#75715E", "#66D9EF", "#A6E22E", "#A6E22E", "#66D9EF"]),
        new("Dracula", true, ["#282A36", "#21222C", "#F8F8F2", "#6272A4", "#343746", "#BD93F9", "#282A36", "#44475A", "#2F3240", "#343746", "#44475A", "#21222C",
            "#FF79C6", "#F1FA8C", "#BD93F9", "#6272A4", "#8BE9FD", "#50FA7B", "#BD93F9", "#8BE9FD"]),
        new("Nord", true, ["#2E3440", "#272C36", "#D8DEE9", "#7B88A1", "#3B4252", "#88C0D0", "#2E3440", "#434C5E", "#353B49", "#3B4252", "#434C5E", "#272C36",
            "#81A1C1", "#A3BE8C", "#B48EAD", "#616E88", "#8FBCBB", "#88C0D0", "#88C0D0", "#88C0D0"]),
        new("Solarized Light", false, ["#FDF6E3", "#EEE8D5", "#586E75", "#93A1A1", "#E3DCC6", "#268BD2", "#FFFFFF", "#D9D2B6", "#F5EEDA", "#F0E9D0", "#E3DCC6", "#EEE8D5",
            "#859900", "#2AA198", "#D33682", "#93A1A1", "#B58900", "#268BD2", "#CB4B16", "#268BD2"]),
        new("Tokyo Night", true, ["#1A1B26", "#16161E", "#C0CAF5", "#565F89", "#292E42", "#7AA2F7", "#1A1B26", "#283457", "#202335", "#202335", "#2F334D", "#16161E",
            "#BB9AF7", "#9ECE6A", "#FF9E64", "#565F89", "#2AC3DE", "#7AA2F7", "#7AA2F7", "#7DCFFF"]),
        new("One Dark", true, ["#282C34", "#21252B", "#ABB2BF", "#5C6370", "#333842", "#61AFEF", "#282C34", "#3E4451", "#2C313A", "#2C313A", "#3A3F4B", "#21252B",
            "#C678DD", "#98C379", "#D19A66", "#5C6370", "#E5C07B", "#61AFEF", "#E06C75", "#56B6C2"]),
        new("GitHub Dark", true, ["#0D1117", "#161B22", "#C9D1D9", "#8B949E", "#30363D", "#2F81F7", "#FFFFFF", "#1F3A5F", "#161B22", "#1C2128", "#21262D", "#161B22",
            "#FF7B72", "#A5D6FF", "#79C0FF", "#8B949E", "#FFA657", "#D2A8FF", "#79C0FF", "#58A6FF"]),
        new("Catppuccin Mocha", true, ["#1E1E2E", "#181825", "#CDD6F4", "#6C7086", "#313244", "#CBA6F7", "#1E1E2E", "#45475A", "#262637", "#313244", "#3B3C52", "#181825",
            "#CBA6F7", "#A6E3A1", "#FAB387", "#6C7086", "#F9E2AF", "#89B4FA", "#F5C2E7", "#89DCEB"]),
        new("Gruvbox Dark", true, ["#282828", "#1D2021", "#EBDBB2", "#928374", "#3C3836", "#FABD2F", "#282828", "#504945", "#32302F", "#32302F", "#3C3836", "#1D2021",
            "#FB4934", "#B8BB26", "#D3869B", "#928374", "#FABD2F", "#8EC07C", "#FE8019", "#83A598"]),
        new("Solarized Dark", true, ["#002B36", "#073642", "#93A1A1", "#586E75", "#0F4452", "#268BD2", "#FFFFFF", "#0A4A5C", "#06404E", "#0A4452", "#0F4F60", "#073642",
            "#859900", "#2AA198", "#D33682", "#586E75", "#B58900", "#268BD2", "#CB4B16", "#268BD2"]),
        new("Rosé Pine", true, ["#191724", "#1F1D2E", "#E0DEF4", "#6E6A86", "#26233A", "#C4A7E7", "#191724", "#403D52", "#21202E", "#26233A", "#2A273F", "#1F1D2E",
            "#31748F", "#F6C177", "#EA9A97", "#6E6A86", "#9CCFD8", "#EBBCBA", "#C4A7E7", "#9CCFD8"]),
        new("Midnight", true, ["#000000", "#0A0A0A", "#E6E6E6", "#7A7A7A", "#1F1F1F", "#4D9EFF", "#000000", "#1F3B5C", "#111111", "#141414", "#1C1C1C", "#0A0A0A",
            "#FF7AB2", "#FC6A5D", "#D9C97C", "#6C7986", "#5DD8FF", "#67B7A4", "#4D9EFF", "#4D9EFF"]),
        new("GitHub Light", false, ["#FFFFFF", "#F6F8FA", "#24292F", "#6E7781", "#D0D7DE", "#0969DA", "#FFFFFF", "#B6E3FF", "#F6F8FA", "#EAEEF2", "#DDF4FF", "#F6F8FA",
            "#CF222E", "#0A3069", "#0550AE", "#6E7781", "#953800", "#8250DF", "#0550AE", "#0969DA"]),
        new("Gruvbox Light", false, ["#FBF1C7", "#EBDBB2", "#3C3836", "#928374", "#D5C4A1", "#B57614", "#FBF1C7", "#D5C4A1", "#F2E5BC", "#EBDBB2", "#D5C4A1", "#EBDBB2",
            "#9D0006", "#79740E", "#8F3F71", "#928374", "#B57614", "#427B58", "#AF3A03", "#076678"]),
        new("Papier", false, ["#FAF7F0", "#F0EBDD", "#3B3A36", "#9A9588", "#E3DDCB", "#B5532A", "#FFFFFF", "#E6D9B8", "#F4EFE2", "#EBE5D3", "#E3DDCB", "#F0EBDD",
            "#B5532A", "#4F7942", "#A0522D", "#9A9588", "#2F6F8F", "#7A4E9C", "#B5532A", "#2F6F8F"]),
    ];

    public static Theme Find(string? name) => All.FirstOrDefault(t => t.Name == name) ?? All[0];

    public static void Apply(Theme theme)
    {
        var resources = Application.Current.Resources;
        for (var i = 0; i < Keys.Length; i++)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme.Colors[i]));
            brush.Freeze();
            resources[Keys[i]] = brush;
        }
    }

    public static Brush Get(string key) => (Brush)Application.Current.Resources[key];
}
