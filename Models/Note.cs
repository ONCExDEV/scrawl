using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Morrow.Models;

public sealed class Note : INotifyPropertyChanged
{
    private string _title = "Untitled note";
    private string _content = string.Empty;
    private bool _isPinned;
    private DateTime _updatedAt = DateTime.Now;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get => _title; set => Set(ref _title, string.IsNullOrWhiteSpace(value) ? "Untitled note" : value.Trim()); }
    public string Content { get => _content; set => Set(ref _content, value ?? string.Empty); }
    public bool IsPinned { get => _isPinned; set => Set(ref _isPinned, value); }
    public DateTime UpdatedAt { get => _updatedAt; set => Set(ref _updatedAt, value); }
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
