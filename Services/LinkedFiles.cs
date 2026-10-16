using System.IO;
using System.Text;
using Scrawl.Models;

namespace Scrawl.Services;

/// <summary>
/// Notes opened from a file stay linked to it: every save of the note also writes the file,
/// in the file's own encoding and line endings. Only notes that changed are written.
/// </summary>
public sealed class LinkedFiles
{
    private sealed record State(string Disk, Encoding Encoding, bool CrLf);

    /// <summary>What a file contained when it was read.</summary>
    public sealed record Snapshot(string Text, Encoding Encoding, bool CrLf);

    private readonly Dictionary<Guid, State> _states = new();

    static LinkedFiles() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Reads a text file. Text is normalised to "\n"; encoding and line endings are remembered for writing back.</summary>
    public static Snapshot Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        string text;
        Encoding encoding;
        try
        {
            // Strict UTF-8 first (also detects BOMs, including UTF-16). Anything else is treated as Windows-1252 so no characters get mangled.
            using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true);
            text = reader.ReadToEnd();
            encoding = reader.CurrentEncoding;
            if (encoding is UTF8Encoding && encoding.GetPreamble().Length == 0) encoding = new UTF8Encoding(false);
        }
        catch (DecoderFallbackException)
        {
            encoding = Encoding.GetEncoding(1252);
            text = encoding.GetString(bytes);
        }
        var crlf = text.Contains("\r\n") || !text.Contains('\n');
        return new Snapshot(text.Replace("\r\n", "\n"), encoding, crlf);
    }

    /// <summary>Remember that the note and its file are in sync with this content.</summary>
    public void Track(Note note, Snapshot snapshot) => _states[note.Id] = new State(snapshot.Text, snapshot.Encoding, snapshot.CrLf);

    public void Untrack(Note note) => _states.Remove(note.Id);

    /// <summary>After starting: reconnect notes to their files. A file edited elsewhere since the note was last saved wins.</summary>
    public void Adopt(IEnumerable<Note> notes)
    {
        foreach (var note in notes.Where(n => n.FilePath is not null && !n.IsLocked))
        {
            try
            {
                if (!File.Exists(note.FilePath)) { note.FilePath = null; continue; }
                var snapshot = Read(note.FilePath!);
                if (snapshot.Text != note.Content && File.GetLastWriteTime(note.FilePath!) > note.UpdatedAt) note.Content = snapshot.Text;
                Track(note, snapshot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Keep the note as it is; the next save tries again.
            }
        }
    }

    /// <summary>Writes every linked note whose text changed. Returns a short message when something went wrong.</summary>
    public string? Flush(IEnumerable<Note> notes)
    {
        string? problem = null;
        foreach (var note in notes.Where(n => n.FilePath is not null && !n.IsLocked))
        {
            _states.TryGetValue(note.Id, out var state);
            if (state is not null && state.Disk == note.Content) continue;
            try
            {
                if (!File.Exists(note.FilePath))
                {
                    note.FilePath = null;
                    _states.Remove(note.Id);
                    problem = $"“{note.Title}” was removed from disk, so it is no longer linked to a file";
                    continue;
                }
                var encoding = state?.Encoding ?? new UTF8Encoding(false);
                var crlf = state?.CrLf ?? true;
                File.WriteAllText(note.FilePath!, crlf ? note.Content.Replace("\n", "\r\n") : note.Content, encoding);
                _states[note.Id] = new State(note.Content, encoding, crlf);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problem = $"Couldn't save “{note.Title}” to its file";
            }
        }
        return problem;
    }
}
