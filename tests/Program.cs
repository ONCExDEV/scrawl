using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Morrow;
using Morrow.Models;
using Morrow.Services;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new App();
        app.InitializeComponent();
        _ = new MainWindow();
        Console.WriteLine("PASS: main window XAML initializes");

        var dataDirectory = Path.Combine(Environment.CurrentDirectory, $".morrow-smoke-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("MORROW_DATA_DIR", dataDirectory);
        try
        {
            var note = new Note { Title = "A useful thought", Content = "Some words\n```csharp\nvar answer = 42;\n```", IsPinned = true };
            var store = new NoteStore();
            store.SaveAsync([note]).GetAwaiter().GetResult();
            var loaded = store.LoadAsync().GetAwaiter().GetResult();
            Assert(loaded.Count == 1, "one saved note loads");
            Assert(loaded[0].Id == note.Id, "note id survives saving");
            Assert(loaded[0].Title == note.Title && loaded[0].Content == note.Content, "note text survives saving");
            Assert(loaded[0].IsPinned, "pinned state survives saving");
            Assert(!File.Exists(Path.Combine(dataDirectory, "notes.json.tmp")), "atomic save leaves no temporary file");

            var preview = new StackPanel();
            MarkdownRenderer.Render(preview, "# Welcome\n\nA **clear** note with `inline code`.\n\n- one item\n\n```csharp\nvar answer = 42;\n```");
            Assert(FindText(preview, "Welcome"), "Markdown heading renders");
            Assert(preview.Children.OfType<Border>().Any(), "code block renders in a code card");
            Assert(FindText(preview, "C#") || FindText(preview, "CSHARP"), "code language label renders");
            Assert(FindText(preview, "answer"), "code content renders");

            MarkdownRenderer.Render(preview, "    const answer = 42;");
            Assert(preview.Children.OfType<Border>().Any(), "indented code is detected automatically");
            MarkdownRenderer.Render(preview, "var message = \"Hello, Morrow!\";\nConsole.WriteLine(message);");
            Assert(preview.Children.OfType<Border>().Any(), "a clear unwrapped code snippet is detected automatically");
            Assert(FindText(preview, "CSHARP"), "language is inferred for an unwrapped C# snippet");
            var purple = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#7768E8");
            Assert(Descendants<TextBlock>(preview).SelectMany(block => block.Inlines.OfType<Run>()).Any(run => run.Text == "var" && run.Foreground is System.Windows.Media.SolidColorBrush brush && brush.Color == purple), "code keywords receive syntax colors");
            Console.WriteLine("PASS: notes save, reload, and preserve their fields");
            Console.WriteLine("PASS: Markdown, fenced and indented code, and automatic C# detection render");
        }
        finally
        {
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static bool FindText(DependencyObject root, string text)
    {
        if (root is TextBlock block && (block.Text + string.Concat(block.Inlines.OfType<Run>().Select(run => run.Text))).Contains(text, StringComparison.OrdinalIgnoreCase)) return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindText(VisualTreeHelper.GetChild(root, i), text)) return true;
        return false;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static void Assert(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException($"FAIL: {description}");
    }
}
