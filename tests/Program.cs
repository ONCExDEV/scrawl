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
        // Never talk to a Scrawl the user has open (single-instance mutex and pipe are named after this id).
        Environment.SetEnvironmentVariable("SCRAWL_INSTANCE", "tests-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("SCRAWL_NO_WINDOW", "1");
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

            // Notes opened from a file save back to it (the "edit, then reopen the file" case).
            var linked = new LinkedFiles();
            var textFile = Path.Combine(dataDirectory, "exported.txt");
            File.WriteAllText(textFile, "first line\r\nsecond line");
            var snapshot = LinkedFiles.Read(textFile);
            var fileNote = new Note { Title = "exported", Content = snapshot.Text, FilePath = textFile };
            linked.Track(fileNote, snapshot);
            Assert(linked.Flush([fileNote]) is null && File.ReadAllText(textFile) == "first line\r\nsecond line", "an unchanged linked note does not touch its file");
            fileNote.Content = "changed text\nsecond line";
            Assert(linked.Flush([fileNote]) is null, "saving a linked note succeeds");
            Assert(File.ReadAllText(textFile) == "changed text\r\nsecond line", "edits are written back to the file and Windows line endings are kept");
            Assert(LinkedFiles.Read(textFile).Text == fileNote.Content, "reopening the file shows the edited text");

            var unixFile = Path.Combine(dataDirectory, "unix.txt");
            File.WriteAllText(unixFile, "a\nb\n");
            var unixSnapshot = LinkedFiles.Read(unixFile);
            var unixNote = new Note { Content = unixSnapshot.Text + "c\n", FilePath = unixFile };
            linked.Track(unixNote, unixSnapshot);
            linked.Flush([unixNote]);
            Assert(File.ReadAllText(unixFile) == "a\nb\nc\n", "LF files stay LF");

            var ansiFile = Path.Combine(dataDirectory, "ansi.txt");
            File.WriteAllBytes(ansiFile, [0x63, 0x61, 0x66, 0xE9]);   // "café" in Windows-1252, not valid UTF-8
            var ansiSnapshot = LinkedFiles.Read(ansiFile);
            Assert(ansiSnapshot.Text == "café", "non-UTF-8 files are read without garbling");
            var ansiNote = new Note { Content = "café!", FilePath = ansiFile };
            linked.Track(ansiNote, ansiSnapshot);
            linked.Flush([ansiNote]);
            Assert(File.ReadAllBytes(ansiFile).SequenceEqual(new byte[] { 0x63, 0x61, 0x66, 0xE9, 0x21 }), "files keep their original encoding when saved");

            File.Delete(textFile);
            fileNote.Content = "text after the file was deleted";
            Assert(linked.Flush([fileNote]) is not null && fileNote.FilePath is null && !File.Exists(textFile), "a file deleted elsewhere is not recreated; the note is unlinked");
            Console.WriteLine("PASS: notes opened from a file are saved back to that file");

            // The whole flow through the real window, without ever showing it: open a file, edit it in the pane, save, reopen.
            var flowFile = Path.Combine(dataDirectory, "from-explorer.txt");
            File.WriteAllText(flowFile, "original text\r\nsecond line");
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var window = new MainWindow { StartupFiles = [flowFile] };
            var nonPublic = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            Pump((Task)typeof(MainWindow).GetMethod("LoadAsync", nonPublic)!.Invoke(window, null)!);
            var panes = (List<Scrawl.Controls.NotePane>)typeof(MainWindow).GetField("_panes", nonPublic)!.GetValue(window)!;
            Assert(panes.Count == 1 && panes[0].Note?.FilePath == Path.GetFullPath(flowFile), "opening a file shows it in a pane, linked to the file");
            panes[0].Editor.Text = "edited in Scrawl";
            Pump((Task)typeof(MainWindow).GetMethod("SaveAsync", nonPublic)!.Invoke(window, null)!);
            Assert(File.ReadAllText(flowFile) == "edited in Scrawl", "typing in the pane is saved to the file by auto-save");
            File.WriteAllText(flowFile, "changed by another program");
            Pump((Task)typeof(MainWindow).GetMethod("ImportPathsAsync", nonPublic)!.Invoke(window, [new[] { flowFile }])!);
            Assert(panes.Count == 1 && panes[0].Editor.Text == "changed by another program", "reopening the file shows its current text instead of an old copy");
            Console.WriteLine("PASS: open a file, edit it, auto-save and reopen all work through the window");

            // Download, then open the downloaded file: it must stay the same note, never a second copy.
            var downloads = Path.Combine(dataDirectory, "downloads");
            Environment.SetEnvironmentVariable("SCRAWL_DOWNLOADS_DIR", downloads);
            var allNotes = (System.Collections.ObjectModel.ObservableCollection<Note>)typeof(MainWindow).GetField("_notes", nonPublic)!.GetValue(window)!;
            var quickExport = typeof(MainWindow).GetMethod("QuickExport", nonPublic)!;
            var addNote = typeof(MainWindow).GetMethod("Add", nonPublic)!;
            var shopping = new Note { Title = "Shopping", Content = "milk\neggs" };
            addNote.Invoke(window, [shopping]);
            quickExport.Invoke(window, [shopping, ".txt"]);
            var shoppingFile = Path.Combine(downloads, "Shopping.txt");
            Assert(File.Exists(shoppingFile) && shopping.FilePath == shoppingFile, "downloading a note links it to the downloaded file");
            shopping.Content = "milk\neggs\nbread";
            quickExport.Invoke(window, [shopping, ".txt"]);
            Assert(Directory.GetFiles(downloads).Length == 1 && File.ReadAllText(shoppingFile) == "milk\r\neggs\r\nbread", "downloading again updates the same file instead of making 'Shopping (2).txt'");
            var noteCount = allNotes.Count;
            Pump((Task)typeof(MainWindow).GetMethod("ImportPathsAsync", nonPublic)!.Invoke(window, [new[] { shoppingFile }])!);
            Assert(allNotes.Count == noteCount, "opening the downloaded file does not add a second note");

            var todo = new Note { Title = "Todo", Content = "one\ntwo" };   // exported by an older version: same name and text, no link
            addNote.Invoke(window, [todo]);
            var todoFile = Path.Combine(downloads, "Todo.txt");
            File.WriteAllText(todoFile, "one\r\ntwo");
            noteCount = allNotes.Count;
            Pump((Task)typeof(MainWindow).GetMethod("ImportPathsAsync", nonPublic)!.Invoke(window, [new[] { todoFile }])!);
            Assert(allNotes.Count == noteCount && todo.FilePath == todoFile, "a file that matches an existing note by name and text is adopted by that note");
            Console.WriteLine("PASS: downloading and reopening never creates duplicate notes");
        }
        finally
        {
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
            GC.KeepAlive(app);
        }
    }

    /// <summary>Waits for a UI-bound task while keeping the dispatcher running.</summary>
    private static void Pump(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(5);
        }
        if (!task.IsCompleted) throw new InvalidOperationException("FAIL: the operation did not finish");
        task.GetAwaiter().GetResult();
    }

    private static void Assert(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException($"FAIL: {description}");
    }
}
