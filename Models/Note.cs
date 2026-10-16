using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Scrawl.Models;

public sealed class Note : INotifyPropertyChanged
{
    private string _title = "Untitled note";
    private string _content = string.Empty;
    private bool _isPinned;
    private string _language = "text";
    private string? _lockSalt;
    private string? _filePath;
    private byte[]? _key;
    private DateTime _updatedAt = DateTime.Now;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get => _title; set => Set(ref _title, string.IsNullOrWhiteSpace(value) ? "Untitled note" : value.Trim()); }
    public string Content { get => _content; set { if (Set(ref _content, value ?? string.Empty)) Raise(nameof(Snippet)); } }
    public string Language { get => _language; set => Set(ref _language, string.IsNullOrWhiteSpace(value) || value == "markdown" ? "text" : value); }
    public bool IsPinned { get => _isPinned; set => Set(ref _isPinned, value); }
    public DateTime UpdatedAt { get => _updatedAt; set { if (Set(ref _updatedAt, value)) Raise(nameof(Stamp)); } }

    /// <summary>Salt of the password; null when the note has no password.</summary>
    public string? LockSalt { get => _lockSalt; set { if (Set(ref _lockSalt, value)) { Raise(nameof(IsLocked)); Raise(nameof(Snippet)); } } }

    /// <summary>The file this note was opened from. Saving the note also saves this file.</summary>
    public string? FilePath { get => _filePath; set { if (Set(ref _filePath, value)) Raise(nameof(IsLinked)); } }

    [JsonIgnore] public bool IsLinked => _filePath is not null;

    /// <summary>The encrypted text of a password protected note.</summary>
    public string? Vault { get; set; }

    /// <summary>The unlock key. Only exists in memory while the note is unlocked.</summary>
    [JsonIgnore]
    public byte[]? Key { get => _key; set { _key = value; Raise(nameof(IsUnlocked)); Raise(nameof(Snippet)); } }

    [JsonIgnore] public bool IsLocked => _lockSalt is not null;
    [JsonIgnore] public bool IsUnlocked => _key is not null;

    /// <summary>First non-empty line of the note, shown in the list.</summary>
    [JsonIgnore]
    public string Snippet
    {
        get
        {
            if (IsLocked && !IsUnlocked) return "Locked";
            foreach (var raw in _content.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length > 0) return line.Length > 90 ? line[..90] : line;
            }
            return "No additional text";
        }
    }

    [JsonIgnore]
    public string Stamp => _updatedAt.Date == DateTime.Today ? _updatedAt.ToString("HH:mm")
        : _updatedAt.Date == DateTime.Today.AddDays(-1) ? "Yesterday"
        : _updatedAt.ToString(_updatedAt.Year == DateTime.Today.Year ? "MMM d" : "MMM d, yyyy");

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(propertyName!);
        return true;
    }
}
