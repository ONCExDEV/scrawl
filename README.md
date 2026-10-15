# Morrow

A calm, lightweight notes app for Windows. Morrow opens straight into your notes, saves them on your device, and gives Markdown and code a clear live preview.

## Features

- Fast native Windows desktop app built with C# and WPF
- Automatic saving to a small local JSON file
- Search note titles and contents
- Pin important notes to the top of the list
- Live Markdown preview for headings, lists, emphasis, links, and inline code
- Syntax-colored fenced, indented, and clearly recognizable pasted code, with simple language detection
- Import and export Markdown files; copy a note as Markdown
- Word and line count, with keyboard shortcuts for new note, search, and save
- No account, network connection, browser runtime, or third-party package required

## Requirements

- Windows 10 or 11
- .NET 8 Desktop Runtime

## Run from source

1. Install the .NET 8 SDK.
2. Clone this repository and open its folder in a terminal.
3. Run the app:

   ```powershell
   dotnet run --project Morrow.csproj
   ```

To create a Windows x64 build that uses the installed .NET Desktop Runtime:

```powershell
dotnet publish Morrow.csproj -c Release -r win-x64 --self-contained false
```

The publish output is in `bin/Release/net8.0-windows/win-x64/publish/`.

Run the lightweight storage and rendering checks with:

```powershell
dotnet run --project tests/Morrow.SmokeTests.csproj
```

## Use Markdown and code

Write Markdown in the left editor. The preview updates as you type. Add a language after the opening backticks for more accurate code coloring:

````markdown
```csharp
var message = "Hello, Morrow!";
Console.WriteLine(message);
```
````

Fenced blocks without a language, indented Markdown code blocks, and clear pasted code snippets receive basic automatic language detection. Use a language label when you want predictable highlighting.

## Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| `Ctrl+N` | Create a note |
| `Ctrl+F` | Search notes |
| `Ctrl+S` | Save now |

## Storage

Notes are stored locally at `%LOCALAPPDATA%\Morrow\notes.json`. Import and export use ordinary Markdown files, so your writing remains portable and readable outside the app.

## Project structure

```text
Morrow/
├── Models/Note.cs                    # Note data and change notifications
├── Services/NoteStore.cs             # Local JSON storage
├── Services/MarkdownRenderer.cs      # Markdown and code preview
├── tests/Program.cs                   # Storage and rendering smoke checks
├── App.xaml                          # Colors and shared controls
├── MainWindow.xaml                   # App layout
└── MainWindow.xaml.cs                # Note editing and app behavior
```

## License

Morrow is available under the MIT License. See [LICENSE](LICENSE).
