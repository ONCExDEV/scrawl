using System.IO;
using System.Text.Json;
using Scrawl.Models;

namespace Scrawl.Services;

public sealed class NoteStore
{
    /// <summary>What is written to disk. Locked notes never write their text, only the encrypted vault.</summary>
    private sealed record Stored(Guid Id, string Title, string Content, string Language, bool IsPinned, DateTime UpdatedAt, string? LockSalt, string? Vault, string? FilePath = null);

    public static string DataDirectory => Environment.GetEnvironmentVariable("SCRAWL_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Scrawl");

    static NoteStore() => MigrateOldFolder();

    /// <summary>The app used to be called "Mor" + "row". Copy its notes over once so nothing is lost.</summary>
    private static void MigrateOldFolder()
    {
        try
        {
            if (Environment.GetEnvironmentVariable("SCRAWL_DATA_DIR") is not null) return;
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var oldFolder = Path.Combine(root, "Mor" + "row");
            var newFolder = Path.Combine(root, "Scrawl");
            if (!Directory.Exists(oldFolder) || Directory.Exists(newFolder)) return;
            Directory.CreateDirectory(newFolder);
            foreach (var file in Directory.GetFiles(oldFolder)) File.Copy(file, Path.Combine(newFolder, Path.GetFileName(file)));
        }
        catch { /* migration is best effort; the app starts fresh if it fails */ }
    }

    private readonly string _filePath = Path.Combine(DataDirectory, "notes.json");
    private readonly object _writeLock = new();
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public bool Exists => File.Exists(_filePath);

    public async Task<List<Note>> LoadAsync()
    {
        if (!File.Exists(_filePath)) return [];
        await using var stream = File.OpenRead(_filePath);
        var stored = await JsonSerializer.DeserializeAsync<List<Stored>>(stream, Options) ?? [];
        return stored.Select(s => new Note
        {
            Id = s.Id, Title = s.Title, Content = s.LockSalt is null ? s.Content : string.Empty, Language = s.Language,
            IsPinned = s.IsPinned, UpdatedAt = s.UpdatedAt, Vault = s.Vault, LockSalt = s.LockSalt, FilePath = s.FilePath
        }).ToList();
    }

    /// <summary>Seals locked notes and copies everything into plain records, on the calling (UI) thread.</summary>
    private static List<Stored> Snapshot(IEnumerable<Note> notes)
    {
        var stored = new List<Stored>();
        foreach (var note in notes)
        {
            if (note.IsLocked && note.Key is not null) note.Vault = NoteLock.Encrypt(note.Content, note.Key);
            stored.Add(new Stored(note.Id, note.Title, note.IsLocked ? string.Empty : note.Content, note.Language, note.IsPinned, note.UpdatedAt, note.LockSalt, note.IsLocked ? note.Vault : null, note.FilePath));
        }
        return stored;
    }

    private void Write(List<Stored> stored)
    {
        lock (_writeLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var temporaryPath = _filePath + ".tmp";
            File.WriteAllBytes(temporaryPath, JsonSerializer.SerializeToUtf8Bytes(stored, Options));
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
    }

    /// <summary>Saves without blocking the UI thread.</summary>
    public Task SaveAsync(IEnumerable<Note> notes)
    {
        var stored = Snapshot(notes);
        return Task.Run(() => Write(stored));
    }

    /// <summary>Saves right now. Used when the window closes (never wait on an async save from the UI thread: that deadlocks).</summary>
    public void Save(IEnumerable<Note> notes) => Write(Snapshot(notes));
}
