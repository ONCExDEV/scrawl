using System.IO;
using System.Windows;
using System.Windows.Threading;
using Scrawl.Controls;
using Scrawl;
using Scrawl.Models;
using Scrawl.Services;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new App();
        app.InitializeComponent();
        _ = new MainWindow();
        Console.WriteLine("PASS: main window XAML initializes");

        var dataDirectory = Path.Combine(Environment.CurrentDirectory, $".scrawl-smoke-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("SCRAWL_DATA_DIR", dataDirectory);
        try
        {
            var note = new Note { Title = "A useful thought", Content = "var answer = 42;", Language = "csharp", IsPinned = true };
            var store = new NoteStore();
            store.SaveAsync([note]).GetAwaiter().GetResult();
            var loaded = store.LoadAsync().GetAwaiter().GetResult();
            Assert(loaded.Count == 1, "one saved note loads");
            Assert(loaded[0].Id == note.Id, "note id survives saving");
            Assert(loaded[0].Title == note.Title && loaded[0].Content == note.Content, "note text survives saving");
            Assert(loaded[0].IsPinned && loaded[0].Language == "csharp", "pinned state and language survive saving");
            Assert(!File.Exists(Path.Combine(dataDirectory, "notes.json.tmp")), "atomic save leaves no temporary file");
            Assert(new Note { Language = "markdown" }.Language == "text", "legacy markdown notes become plain notes");
            Console.WriteLine("PASS: notes save, reload, and preserve their fields");

            Assert(CodeHighlighter.Tokenize("const x = 42 + \"hi\"; // note", "javascript").Select(t => t.Kind).Distinct().Count() >= 4, "highlighter finds keywords, numbers, strings and comments");
            Assert(!CodeHighlighter.Tokenize("const x = 1;", "text").Any(), "plain text is never colored");
            Assert(CodeHighlighter.Guess("using System;\nnamespace Demo;\npublic class A { }") == "csharp", "C# is detected");
            Assert(CodeHighlighter.Guess("just a grocery list\nmilk\neggs") is null, "ordinary text is not mistaken for code");
            Assert(Themes.All.All(theme => theme.Colors.Length == Themes.Keys.Length), "every theme defines every color");
            Console.WriteLine("PASS: syntax highlighting, language detection and themes");

            var first = new Note { Title = "First", Content = string.Join('\n', "alpha", "  beta"), Language = "csharp" };
            var second = new Note { Title = "Second", Content = "gamma", Language = "text" };
            var pane = new NotePane();
            pane.Load(first);
            pane.Load(second);
            pane.Load(first);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Assert(first.Content == string.Join('\n', "alpha", "  beta") && second.Content == "gamma", "switching notes in a pane never changes their text");
            Assert(first.Language == "csharp" && second.Language == "text", "switching notes in a pane never changes their language");
            pane.Editor.Document.Insert(0, "x");
            Assert(first.Content.StartsWith("xalpha") && second.Content == "gamma", "typing edits only the note shown in the pane");
            Console.WriteLine("PASS: panes keep notes separate");

            var secret = new Note { Title = "Secret", Content = "the launch code is 1234" };
            NoteLock.Lock(secret, "hunter2");
            Assert(secret.IsLocked && !secret.IsUnlocked && secret.Content.Length == 0, "locking clears the text from memory");
            store.SaveAsync([secret]).GetAwaiter().GetResult();
            var onDisk = File.ReadAllText(Path.Combine(dataDirectory, "notes.json"));
            Assert(!onDisk.Contains("launch code"), "a locked note is never written as plain text");
            var reloaded = store.LoadAsync().GetAwaiter().GetResult()[0];
            Assert(reloaded.IsLocked && !NoteLock.TryUnlock(reloaded, "wrong"), "a wrong password does not unlock");
            Assert(NoteLock.TryUnlock(reloaded, "hunter2") && reloaded.Content == "the launch code is 1234", "the right password restores the text");
            Assert(NoteLock.Verify(reloaded, "hunter2") && !NoteLock.Verify(reloaded, "nope"), "passwords can be verified without unlocking");
            reloaded.Content = "changed";
            store.SaveAsync([reloaded]).GetAwaiter().GetResult();
            var again = store.LoadAsync().GetAwaiter().GetResult()[0];
            Assert(NoteLock.TryUnlock(again, "hunter2") && again.Content == "changed", "edits to an unlocked note are saved encrypted");
            Console.WriteLine("PASS: password locked notes are encrypted on disk");
        }
        finally
        {
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static void Assert(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException($"FAIL: {description}");
    }
}
