using System.IO;
using System.Net.Http;
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
            Assert(linked.Flush([fileNote]) is null && !File.Exists(textFile), "a file deleted elsewhere is not recreated by auto-save");
            Assert(linked.Missing([fileNote]).Contains(fileNote) && fileNote.FilePath == textFile, "the note stays linked and is reported as missing, so the user can decide");
            linked.Restore(fileNote);
            Assert(File.ReadAllText(textFile) == "text after the file was deleted" && !linked.Missing([fileNote]).Any(), "restoring writes the note's text back to the deleted file");
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

            // A new file that looks like code opens as a Code note; plain text stays Notepad; reopening keeps the choice.
            var codeFile = Path.Combine(downloads, "program.txt");
            File.WriteAllText(codeFile, "using System;\r\nnamespace Demo;\r\npublic class Greeter { }\r\n");
            var proseFile = Path.Combine(downloads, "letter.txt");
            File.WriteAllText(proseFile, "Dear Sam,\r\nthanks for the lovely evening, it was great to see you again.\r\n");
            var importPaths = typeof(MainWindow).GetMethod("ImportPathsAsync", nonPublic)!;
            Pump((Task)importPaths.Invoke(window, [new[] { codeFile, proseFile }])!);
            var codeNote = allNotes.Single(n => n.FilePath == codeFile);
            var proseNote = allNotes.Single(n => n.FilePath == proseFile);
            Assert(codeNote.Language == "csharp" && codeNote.OpenedFromFile, "a new code file opens as a Code note");
            Assert(proseNote.Language == "text", "a new text file stays a Notepad note");
            codeNote.Language = "text";
            Pump((Task)importPaths.Invoke(window, [new[] { codeFile }])!);
            Assert(codeNote.Language == "text", "reopening a file does not change the type the user chose");
            Console.WriteLine("PASS: code files are detected when they are opened for the first time");

            // A file deleted while its note is open: the window must ask instead of silently keeping text for a file that is gone.
            var checkMissing = typeof(MainWindow).GetMethod("CheckMissingFilesAsync", nonPublic)!;
            void Click(string handler) => typeof(MainWindow).GetMethod(handler, nonPublic)!.Invoke(window, [null, new System.Windows.RoutedEventArgs()]);
            window.WelcomeLayer.Visibility = System.Windows.Visibility.Collapsed;   // the first-run welcome is showing in this fresh window; questions wait until it is dismissed
            var gonePath = Path.Combine(downloads, "gone.txt");
            File.WriteAllText(gonePath, "text of a file that is about to be deleted");
            Pump((Task)importPaths.Invoke(window, [new[] { gonePath }])!);
            var goneNote = allNotes.Single(n => n.FilePath == gonePath);
            File.Delete(gonePath);
            var asking = (Task)checkMissing.Invoke(window, null)!;
            Assert(window.DialogLayer.Visibility == System.Windows.Visibility.Visible && window.DialogText.Text.Contains("deleted from disk"), "deleting an open file makes Scrawl ask what to do");
            Assert(window.DialogCancel.Content as string == "Close note" && window.DialogOk.Content as string == "Restore file", "the choices are restoring the file or closing the note");
            Click("DialogCancel_Click");       // Close note
            Pump(asking);
            Assert(!allNotes.Contains(goneNote), "closing the note of a deleted file removes it from Scrawl");

            var backPath = Path.Combine(downloads, "undo-me.txt");
            File.WriteAllText(backPath, "bring me back");
            Pump((Task)importPaths.Invoke(window, [new[] { backPath }])!);
            var backNote = allNotes.Single(n => n.FilePath == backPath);
            File.Delete(backPath);
            asking = (Task)checkMissing.Invoke(window, null)!;
            Click("DialogOk_Click");           // Restore file
            Pump(asking);
            Assert(File.Exists(backPath) && File.ReadAllText(backPath) == "bring me back" && allNotes.Contains(backNote), "undoing the deletion brings the file back from the note and keeps the note");

            var ownNote = new Note { Title = "own", Content = "an app note that was downloaded", FilePath = Path.Combine(downloads, "own.txt") };   // exported earlier: not created from a file
            File.WriteAllText(ownNote.FilePath, ownNote.Content);
            addNote.Invoke(window, [ownNote]);
            File.Delete(ownNote.FilePath);
            asking = (Task)checkMissing.Invoke(window, null)!;
            Assert(window.DialogCancel.Content as string == "Keep note", "a note that was only exported can be kept without its file");
            Click("DialogCancel_Click");
            Pump(asking);
            Assert(allNotes.Contains(ownNote) && ownNote.FilePath is null && ownNote.Content == "an app note that was downloaded", "keeping the note unlinks it and loses nothing");
            Console.WriteLine("PASS: deleting a file that is open asks to restore it or close the note");

            // Updates: a fake GitHub, so no network is used. Newer release found, right asset chosen, download verified, exe swapped in place.
            SynchronizationContext.SetSynchronizationContext(null);   // these tests wait on tasks; with the dispatcher context they would wait on themselves
            var exeBytes = new byte[2048];
            exeBytes[0] = (byte)'M'; exeBytes[1] = (byte)'Z';
            var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(exeBytes)).ToLowerInvariant();
            string Release(string tag, string digest) => "{\"tag_name\":\"" + tag + "\",\"assets\":["
                + "{\"name\":\"Scrawl.exe\",\"size\":2048,\"digest\":\"sha256:" + digest + "\",\"browser_download_url\":\"https://github.com/ONCExDEV/scrawl/releases/download/" + tag + "/Scrawl.exe\"},"
                + "{\"name\":\"Scrawl-standalone.exe\",\"size\":99,\"browser_download_url\":\"https://github.com/ONCExDEV/scrawl/releases/download/" + tag + "/Scrawl-standalone.exe\"}]}";
            var fake = new FakeGitHub { Json = Release("v9.9.9", sha), Bytes = exeBytes };
            var updater = new Updater(new HttpClient(fake));
            Assert(Updater.Current >= new Version(1, 0, 8), "the running app knows its own version");
            Assert(await_(updater.CheckAsync(false, new Version(9, 9, 9))) is null, "no update is offered when the latest release is the version you have");
            Assert(await_(updater.CheckAsync(false, new Version(9, 9, 10))) is null, "no update is offered when you are ahead of the latest release");
            var found = await_(updater.CheckAsync(false, new Version(1, 0, 8)));
            Assert(found is { AssetName: "Scrawl.exe", Size: 2048 } && found.Version == new Version(9, 9, 9), "a newer release is found and the small exe is chosen");
            Assert(await_(updater.CheckAsync(true, new Version(1, 0, 8)))?.AssetName == "Scrawl-standalone.exe", "the standalone exe is chosen for a standalone install");
            var downloaded = Path.Combine(dataDirectory, "Scrawl.exe.download");
            await_(updater.DownloadAsync(found!, downloaded));
            Assert(File.ReadAllBytes(downloaded).SequenceEqual(exeBytes), "the download is saved");
            var installed = Path.Combine(dataDirectory, "Scrawl-installed.exe");
            File.WriteAllBytes(installed, [(byte)'M', (byte)'Z', 1, 1]);
            Updater.Replace(downloaded, installed);
            Assert(File.ReadAllBytes(installed).SequenceEqual(exeBytes) && File.Exists(installed + ".old") && !File.Exists(downloaded), "the new exe takes the old one's place and the old one is kept as .old");
            Updater.CleanUp(installed);
            Assert(!File.Exists(installed + ".old"), "leftovers are cleaned up on the next start");

            fake.Json = Release("v9.9.9", new string('0', 64));
            var wrongChecksum = await_(updater.CheckAsync(false, new Version(1, 0, 8)))!;
            var rejected = Path.Combine(dataDirectory, "rejected.download");
            var checksumRejected = false;
            try { await_(updater.DownloadAsync(wrongChecksum, rejected)); } catch (InvalidDataException) { checksumRejected = true; }
            Assert(checksumRejected && !File.Exists(rejected), "a download that does not match GitHub's checksum is rejected and removed");
            var notGitHub = new UpdateInfo(new Version(9, 9, 9), "v9.9.9", "Scrawl.exe", new Uri("https://example.com/Scrawl.exe"), 2048, null);
            var hostRejected = false;
            try { await_(updater.DownloadAsync(notGitHub, rejected)); } catch (InvalidOperationException) { hostRejected = true; }
            Assert(hostRejected && fake.Requests.All(r => r.Host != "example.com"), "downloads from outside GitHub are refused before any request is made");
            Console.WriteLine("PASS: updates are checked, verified and installed in place");
        }
        finally
        {
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
            GC.KeepAlive(app);
        }
    }

    /// <summary>Runs a task to the end on this thread and returns its result.</summary>
    private static T await_<T>(Task<T> task) => task.GetAwaiter().GetResult();
    private static void await_(Task task) => task.GetAwaiter().GetResult();

    /// <summary>Stands in for GitHub: answers the release query and the file download from memory.</summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        public string Json = "{}";
        public byte[] Bytes = [];
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            HttpContent content = request.RequestUri!.AbsolutePath.EndsWith("/latest") ? new StringContent(Json) : new ByteArrayContent(Bytes);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content, RequestMessage = request });
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
