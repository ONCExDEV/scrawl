using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace Scrawl;

public partial class App : Application
{
    private const string PipeName = "Scrawl.OpenFiles";
    private Mutex? _singleInstance;

    /// <summary>
    /// Only one Scrawl runs at a time (two windows would overwrite each other's notes).
    /// A second launch, for example by double-clicking a .txt file, hands its files to the first one and exits.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, "Scrawl.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            SendToRunningInstance(e.Args);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        var window = new MainWindow { StartupFiles = e.Args };
        MainWindow = window;
        window.Show();
        _ = Task.Run(Services.SystemIntegration.RefreshTextFileIcon);
        _ = Task.Run(() => ListenForFiles(window));
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
