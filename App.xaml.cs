using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace Scrawl;

public partial class App : Application
{
    // The tests (and anyone running a second copy on purpose) set SCRAWL_INSTANCE so they never talk to a real Scrawl.
    private static readonly string InstanceSuffix = Environment.GetEnvironmentVariable("SCRAWL_INSTANCE") is { Length: > 0 } id ? "." + id : string.Empty;
    private static readonly string PipeName = "Scrawl.OpenFiles" + InstanceSuffix;
    private Mutex? _singleInstance;

    /// <summary>
    /// Only one Scrawl runs at a time (two windows would overwrite each other's notes).
    /// A second launch, for example by double-clicking a .txt file, hands its files to the first one and exits.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        // The smoke tests drive windows themselves; they must never get a real window of their own on someone's screen.
        if (Environment.GetEnvironmentVariable("SCRAWL_NO_WINDOW") == "1") { base.OnStartup(e); return; }

        // After an update the old process starts the new exe and exits; wait for it so the single-instance check below passes.
        var args = e.Args;
        var restart = Array.IndexOf(args, "--after-update");
        if (restart >= 0 && restart + 1 < args.Length)
        {
            if (int.TryParse(args[restart + 1], out var oldProcess)) WaitForExit(oldProcess);
            args = args.Where((_, i) => i != restart && i != restart + 1).ToArray();
        }

        _singleInstance = new Mutex(true, "Scrawl.SingleInstance" + InstanceSuffix, out var isFirst);
        if (!isFirst)
        {
            SendToRunningInstance(args);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        var window = new MainWindow { StartupFiles = args };
        MainWindow = window;
        window.Show();
        _ = Task.Run(Services.SystemIntegration.RefreshTextFileIcon);
        _ = Task.Run(() => Services.Updater.CleanUp(Services.SystemIntegration.CurrentExe));
        _ = Task.Run(() => ListenForFiles(window));
    }

    private static void WaitForExit(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.WaitForExit(15000);
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    private static void SendToRunningInstance(string[] files)
    {
        try
        {
            // This process was just started by the user (for example by opening a .txt file), so Windows lets it hand the
            // right to come to the front over to the running Scrawl.
            using var current = Process.GetCurrentProcess();
            foreach (var other in Process.GetProcessesByName(current.ProcessName).Where(p => p.Id != current.Id))
            {
                AllowSetForegroundWindow(other.Id);
                other.Dispose();
            }
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client);
            foreach (var file in files) writer.WriteLine(file);
            if (files.Length == 0) writer.WriteLine();
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            // The first instance is not reachable; nothing else to do.
        }
    }

    private static async Task ListenForFiles(MainWindow window)
    {
        while (true)
        {
            try
            {
                await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync();
                using var reader = new StreamReader(server);
                var files = new List<string>();
                while (await reader.ReadLineAsync() is { } line)
                    if (line.Length > 0) files.Add(line);
                await window.Dispatcher.InvokeAsync(() => window.BringToFrontAndOpen(files));
            }
            catch (IOException)
            {
                // A client disconnected early; wait for the next one.
            }
        }
    }
}
