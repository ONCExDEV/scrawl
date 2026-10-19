using System.IO;
using System.Text.Json;

namespace Scrawl.Services;

public sealed class Settings
{
    public string Theme { get; set; } = "Dark+";
    public double FontSize { get; set; } = 14;
    public bool LineNumbersCode { get; set; } = true;
    public bool LineNumbersText { get; set; }
    public bool WordWrap { get; set; } = true;
    public bool SansText { get; set; } = true;
    public bool SidebarCollapsed { get; set; }
    public bool WelcomeDone { get; set; }
    public bool AutoSave { get; set; } = true;
    public bool NotepadLayout { get; set; }
    public string NewNoteKind { get; set; } = "text";
    public string LastCode { get; set; } = "csharp";

    private static string FilePath => Path.Combine(NoteStore.DataDirectory, "settings.json");

    public static Settings Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new() : new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(NoteStore.DataDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch { /* settings are a convenience; never block the app */ }
    }
}
