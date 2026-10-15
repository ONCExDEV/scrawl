using System.IO;
using System.Text.Json;
using Morrow.Models;

namespace Morrow.Services;

public sealed class NoteStore
{
    private readonly string _filePath = Path.Combine(Environment.GetEnvironmentVariable("MORROW_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Morrow"), "notes.json");
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public async Task<List<Note>> LoadAsync()
    {
        if (!File.Exists(_filePath)) return [];
        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<List<Note>>(stream, Options) ?? [];
    }

    public async Task SaveAsync(IEnumerable<Note> notes)
    {
        await _saveLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var temporaryPath = _filePath + ".tmp";
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                await JsonSerializer.SerializeAsync(stream, notes, Options);
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
