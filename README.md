<div align="center">

<img src="docs/icon.png" alt="Scrawl" width="96" height="96">

# Scrawl

**A notepad when you want one, a code editor when you need one.**<br>
Plain notes, code with VS Code colors, and up to four notes side by side.

![Platform](https://img.shields.io/badge/platform-Windows%2010%20%2F%2011-0078d4)
![Built with](https://img.shields.io/badge/built%20with-.NET%208%20%2B%20WPF-512bd4)
![Size](https://img.shields.io/badge/exe-~1%20MB-3fb950)
![License](https://img.shields.io/badge/license-MIT-blue)

<img src="docs/main-dark.png" alt="Scrawl with a Python note next to a plain text note" width="760">

</div>

## Why

Most notes apps are either too plain or too heavy. Scrawl keeps two kinds of notes in one small window: **Notepad** notes that behave exactly like a classic notepad, and **Code** notes with syntax colors, line numbers and auto-indent. Open several at once, lock the private ones with a password, and everything stays on your device.

It is a single ~1 MB executable. No installer, no account, no telemetry. Scrawl only uses the network to ask GitHub once at startup whether a newer version exists (nothing is downloaded), and to update when you press **Update**.

## Features

- **Notepad notes**: plain text only. No colors, no line highlight, no auto-indent. Enter works like a classic notepad
- **Code notes**: syntax colors for C#, JavaScript, TypeScript, Python, Java, C/C++, Go, Rust, JSON, HTML/XML, CSS, SQL and shell. Paste code into a notepad note and the language is detected for you
- **Up to four notes side by side**: drag a note from the list onto the editor and drop it on a tile. Drag the dividers to resize
- **Password or PIN locked notes**: choose a password, or a 4 or 6 digit PIN entered on a number pad (type it twice). Locked notes are encrypted on disk (AES-256-GCM) and blurred until you enter the password or PIN. The number pad also works with your keyboard, and a PIN unlocks as soon as the last digit is in. Deleting a locked note asks for it too
- **Arrange your notes**: drag a note up or down in the list to put it where you want it. Pinned notes always stay on top, and editing a note no longer moves it
- **Automatic saving can be turned off** in Settings: then notes are only saved when you press `Ctrl+S`, and closing with unsaved changes asks first
- **One-click download**: saves the note as `.txt` in your Downloads folder and keeps the note linked to that file, so opening the file later shows the same note and downloading again updates it. Hover the button to export with any extension you type
- **17 themes**: Dark+, Light+, Monokai, Dracula, Nord, Solarized (dark and light), Tokyo Night, One Dark, GitHub (dark and light), Catppuccin Mocha, Gruvbox (dark and light), Rosé Pine, Midnight and Paper
- **Right-click menu** on notes: open, open side by side, pin, rename, duplicate, copy, download, lock, delete
- **File, Edit and View menus** with text tools: sort lines, remove duplicates, change case, insert date, duplicate or delete a line, zoom
- **Update button**: appears (top right) only when GitHub has a newer release, checked once at startup. Pressing it downloads that release, and nothing is downloaded before that. It replaces the exe in the same place and restarts, so your shortcut, text file setting and notes stay exactly as they are
- **Settings**: theme, font size, word wrap, line numbers, what new notes start as, desktop shortcut and text file handling
- **Deleted files are noticed**: if a file you have open is deleted elsewhere, Scrawl asks whether to restore it from the note or close the note
- **Code is recognised**: a file that looks like code opens as a Code note the first time you open it, instead of Notepad
- **Opens files like a notepad**: open a file (`Ctrl+O`, or double-click a `.txt` once Scrawl is set up for text files in Settings) and every edit is saved back to that file automatically, in its own encoding and line endings. Only one Scrawl runs at a time, and opening a file brings it to the front (restoring it if it was minimized)
- Rounded window with macOS style buttons, a notes list that slides away (`Ctrl+B`), search, pinning, automatic saving
- Export notes as `.txt`, `.md` or any extension, or export every note to a folder at once

## Screenshots

<table>
  <tr>
    <td><img src="docs/main-dark.png" alt="Dark theme"></td>
    <td><img src="docs/main-light.png" alt="Light theme"></td>
  </tr>
  <tr>
    <td align="center">Dark+</td>
    <td align="center">Light+</td>
  </tr>
  <tr>
    <td><img src="docs/panes.png" alt="Four notes at once"></td>
    <td><img src="docs/locked.png" alt="A locked note"></td>
  </tr>
  <tr>
    <td align="center">Four notes at once</td>
    <td align="center">Password locked note</td>
  </tr>
  <tr>
    <td><img src="docs/menu.png" alt="File menu"></td>
    <td><img src="docs/settings.png" alt="Settings"></td>
  </tr>
  <tr>
    <td align="center">File menu</td>
    <td align="center">Settings</td>
  </tr>
</table>

## Install

1. Download `Scrawl.exe` from the [latest release](../../releases/latest) (about 1 MB).
2. Put it anywhere and start it.
3. On the first start Scrawl asks whether to create a desktop shortcut and whether to use it for text files.

`Scrawl.exe` needs the [.NET Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0/runtime), version 8 or newer (9 and 10 work too). If it is missing, Windows shows a link to install it. Prefer not to install anything? Download `Scrawl-standalone.exe` instead: the same app with the runtime included (about 70 MB).

> Windows SmartScreen may warn about an unsigned app the first time. Choose "More info", then "Run anyway". The source is in this repository if you prefer to build it yourself (see [Build](#build)).

Requires Windows 10 or 11.

## Using it

| Do this | To get this |
| --- | --- |
| Click a note | Open it in the active pane |
| Drag a note onto the editor | Open it side by side (up to four) |
| Right-click a note | The note menu, without opening it |
| Drag a note up or down the list | Put it where you want it (pinned notes stay on top) |
| Hover the download button | Export with any extension |
| Click the gear | Settings |
| `Notepad` / `Code` above the editor | Switch what kind of note it is |

### Keyboard

| Key | Action |
| --- | --- |
| `Ctrl+N` | New note |
| `Ctrl+O` | Open files (edits are saved back to the file) |
| `Ctrl+S` | Save now (the only way to save when automatic saving is off) |
| `Ctrl+Shift+S` | Export as `.txt` (choose a location) |
| `Ctrl+F` | Search notes |
| `Ctrl+B` or `Ctrl+\` | Show or hide the notes list |
| `Ctrl+W` | Close the active pane |
| `Ctrl+,` | Settings |
| `Ctrl+D` | Duplicate line (code notes) |
| `Ctrl+Shift+K` | Delete line |
| `F5` | Insert date and time |
| `Ctrl++`, `Ctrl+-`, `Ctrl+0`, `Ctrl+wheel` | Zoom |
| `Ctrl+Shift+Del` | Delete note |

## Your data

Notes are saved as you type, and every write goes through a temporary file first, so a crash cannot leave a half-written file. They live in `%LOCALAPPDATA%\Scrawl\notes.json` next to `settings.json`. Set the `SCRAWL_DATA_DIR` environment variable to use another folder.

**Locked notes** are stored encrypted with AES-256-GCM, using a key derived from your password (PBKDF2, 200,000 rounds). Their text is never written to disk in plain form. If you forget a password or PIN the note cannot be recovered. A PIN has only 10,000 (4 digits) or 1,000,000 (6 digits) possible values, so it keeps casual eyes out but a determined person with a copy of your notes file could try them all: use a password for anything that really matters. Titles are not encrypted, only the text.

## Build

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

```powershell
dotnet run --project Scrawl.csproj                       # run
dotnet run --project tests/Scrawl.SmokeTests.csproj      # smoke checks
```

Build the small `Scrawl.exe` (needs the .NET Desktop Runtime, version 8 or newer, to run):

```powershell
dotnet publish Scrawl.csproj -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true -p:PublishReadyToRun=false -o publish
```

For the standalone build with the runtime included, use `-p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true` instead.

Pushing a tag like `v1.0.1` builds both on GitHub and attaches them to a release.

The only dependency is the [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) editor control.

| Path | What is in it |
| --- | --- |
| `MainWindow.xaml(.cs)` | Window, menus, panes, drag and drop, settings |
| `Controls/NotePane.xaml(.cs)` | One note on screen: title, editor and lock screen |
| `Models/Note.cs` | Note data and change notifications |
| `Services/NoteStore.cs` | Local JSON storage |
| `Services/NoteLock.cs` | Password protection (AES-GCM, PBKDF2) |
| `Services/CodeHighlighter.cs` | Tokenizer and language detection |
| `Services/CodeColorizer.cs` | Editor coloring and notepad indentation |
| `Services/Themes.cs` | Theme colors |
| `Services/Settings.cs` | Remembered preferences |
| `Services/SystemIntegration.cs` | Desktop shortcut and "open text files with Scrawl" |
| `tests/` | Smoke checks for storage, highlighting, panes and encryption |

## Contributing

Issues and pull requests are welcome. Please keep the app small and fast, and keep the interface, code and docs in English.

## License

[MIT](LICENSE)
