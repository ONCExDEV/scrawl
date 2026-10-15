using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Scrawl.Services;

/// <summary>Optional Windows integration the user opts into: desktop shortcut and "open text files with Scrawl".</summary>
public static class SystemIntegration
{
    private const string ProgId = "Scrawl.Document";
    private static readonly string[] Extensions = [".txt", ".md", ".log"];

    /// <summary>Text files keep Windows' normal text document icon instead of the Scrawl logo.</summary>
    private const string TextFileIcon = @"%SystemRoot%\system32\imageres.dll,-102";

    private static string ExePath
    {
        get
        {
            var path = Environment.ProcessPath ?? string.Empty;
            // Started through `dotnet Scrawl.dll`: point at the real executable instead of dotnet.exe.
            return Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(AppContext.BaseDirectory, "Scrawl.exe")
                : path;
        }
    }

    public static void CreateDesktopShortcut()
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host is not available.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        var linkPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Scrawl.lnk");
        dynamic link = shell.CreateShortcut(linkPath);
        link.TargetPath = ExePath;
        link.WorkingDirectory = Path.GetDirectoryName(ExePath);
        link.IconLocation = ExePath + ",0";
        link.Description = "Scrawl";
        link.Save();
        Marshal.FinalReleaseComObject(link);
        Marshal.FinalReleaseComObject(shell);
    }

    /// <summary>
    /// Registers Scrawl as an app that can open text files, then opens Windows' "Default apps" page.
    /// Windows does not let an app make itself the default, so the user confirms the choice there.
    /// </summary>
    public static void RegisterAsTextEditor()
    {
        var exe = ExePath;
        using (var progId = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            progId.SetValue(string.Empty, "Scrawl note");
            using var icon = progId.CreateSubKey("DefaultIcon");
            icon.SetValue(string.Empty, TextFileIcon, RegistryValueKind.ExpandString);
            using var command = progId.CreateSubKey(@"shell\open\command");
            command.SetValue(string.Empty, $"\"{exe}\" \"%1\"");
        }

        using (var capabilities = Registry.CurrentUser.CreateSubKey(@"Software\Scrawl\Capabilities"))
        {
            capabilities.SetValue("ApplicationName", "Scrawl");
            capabilities.SetValue("ApplicationDescription", "Notepad and code notes");
            using var associations = capabilities.CreateSubKey("FileAssociations");
            foreach (var extension in Extensions) associations.SetValue(extension, ProgId);
        }

        using (var registered = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            registered.SetValue("Scrawl", @"Software\Scrawl\Capabilities");

        foreach (var extension in Extensions)
        {
            using var openWith = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}\OpenWithProgids");
            openWith.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
    }

    /// <summary>
    /// Earlier versions showed the Scrawl logo on text files. If Scrawl was set up before, switch it to the normal text icon.
    /// Only the icon is touched, never the program path.
    /// </summary>
    public static void RefreshTextFileIcon()
    {
        try
        {
            using var progId = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}", writable: true);
            if (progId is null) return;
            using var icon = progId.CreateSubKey("DefaultIcon");
            if (icon.GetValue(string.Empty, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string == TextFileIcon) return;
            icon.SetValue(string.Empty, TextFileIcon, RegistryValueKind.ExpandString);
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // The icon is cosmetic; ignore registry problems.
        }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
