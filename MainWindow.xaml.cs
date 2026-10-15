using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Morrow.Models;
using Morrow.Services;

namespace Morrow;

public partial class MainWindow : Window
{
    private readonly NoteStore _store = new();
    private readonly ObservableCollection<Note> _notes = [];
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private ICollectionView? _notesView;
    private Note? _selectedNote;
    private bool _isLoading;

    public MainWindow()
    {
        InitializeComponent();
        _saveTimer.Tick += async (_, _) => { _saveTimer.Stop(); await SaveAsync(); };
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); MarkdownRenderer.Render(PreviewPanel, ContentBox.Text); UpdateWordCount(); };
        Loaded += async (_, _) => await LoadAsync();
        Closed += async (_, _) => { _saveTimer.Stop(); await SaveAsync(); };
    }

    private async Task LoadAsync()
    {
        try
        {
            foreach (var note in await _store.LoadAsync())
            {
                note.PropertyChanged += Note_PropertyChanged;
                _notes.Add(note);
            }
            _notesView = CollectionViewSource.GetDefaultView(_notes);
            _notesView.Filter = FilterNote;
            _notesView.SortDescriptions.Add(new SortDescription(nameof(Note.IsPinned), ListSortDirection.Descending));
            _notesView.SortDescriptions.Add(new SortDescription(nameof(Note.UpdatedAt), ListSortDirection.Descending));
            NotesList.ItemsSource = _notesView;
            RefreshNotesView();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Morrow couldn't read your notes.\n\n{ex.Message}", "Couldn't load notes", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool FilterNote(object item)
    {
        if (item is not Note note) return false;
        var query = SearchBox.Text == "Search notes…" ? string.Empty : SearchBox.Text.Trim();
        return query.Length == 0 || note.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || note.Content.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshNotesView()
    {
        _notesView?.Refresh();
        NotesCount.Text = _notes.Count == 1 ? "1 note" : $"{_notes.Count} notes";
    }

    private async Task SaveAsync()
    {
        if (_isLoading) return;
        try
        {
            await _store.SaveAsync(_notes.ToList());
            SavedLabel.Text = "All changes saved";
        }
        catch (Exception ex)
        {
            SavedLabel.Text = "Couldn't save";
            MessageBox.Show($"Your latest changes couldn't be saved.\n\n{ex.Message}", "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void QueueSave()
    {
        if (_isLoading) return;
        SavedLabel.Text = "Saving…";
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SelectNote(Note? note)
    {
        _isLoading = true;
        _selectedNote = note;
        var hasNote = note is not null;
        EmptyState.Visibility = hasNote ? Visibility.Collapsed : Visibility.Visible;
        EditorLayout.Visibility = hasNote ? Visibility.Visible : Visibility.Collapsed;
        PinButton.IsEnabled = hasNote;
        TitleBox.Text = note?.Title ?? string.Empty;
        ContentBox.Text = note?.Content ?? string.Empty;
        PinIcon.Text = note?.IsPinned == true ? "★" : "☆";
        PinIcon.Foreground = note?.IsPinned == true ? System.Windows.Media.Brushes.Goldenrod : (System.Windows.Media.Brush)FindResource("Muted");
        MarkdownRenderer.Render(PreviewPanel, ContentBox.Text);
        UpdateWordCount();
        SavedLabel.Text = "All changes saved";
        _isLoading = false;
    }

    private void NewNote_Click(object sender, RoutedEventArgs e)
    {
        var note = new Note { Title = "Untitled note", UpdatedAt = DateTime.Now };
        note.PropertyChanged += Note_PropertyChanged;
        _notes.Add(note);
        RefreshNotesView();
        NotesList.SelectedItem = note;
        TitleBox.Focus();
        TitleBox.SelectAll();
        QueueSave();
    }

    private void NotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NotesList.SelectedItem is Note note) SelectNote(note);
        else if (NotesList.SelectedItem is null) SelectNote(null);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_notesView is null) return;
        _notesView.Refresh();
        if (NotesList.SelectedItem is null) SelectNote(null);
        else if (!FilterNote(NotesList.SelectedItem)) NotesList.SelectedItem = null;
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (SearchBox.Text == "Search notes…") SearchBox.Clear();
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SearchBox.Text)) SearchBox.Text = "Search notes…";
    }

    private void TitleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _selectedNote is null) return;
        _selectedNote.Title = string.IsNullOrWhiteSpace(TitleBox.Text) ? "Untitled note" : TitleBox.Text;
        _selectedNote.UpdatedAt = DateTime.Now;
        RefreshNotesView();
        QueueSave();
    }

    private void ContentBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _selectedNote is null) return;
        _selectedNote.Content = ContentBox.Text;
        _selectedNote.UpdatedAt = DateTime.Now;
        _notesView?.Refresh();
        _previewTimer.Stop();
        _previewTimer.Start();
        QueueSave();
    }

    private void Note_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Note.IsPinned) or nameof(Note.UpdatedAt))
        {
            _notesView?.Refresh();
            if (sender is Note note && ReferenceEquals(note, _selectedNote)) PinIcon.Text = note.IsPinned ? "★" : "☆";
        }
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedNote is null) return;
        _selectedNote.IsPinned = !_selectedNote.IsPinned;
        _selectedNote.UpdatedAt = DateTime.Now;
        PinIcon.Text = _selectedNote.IsPinned ? "★" : "☆";
        PinIcon.Foreground = _selectedNote.IsPinned ? System.Windows.Media.Brushes.Goldenrod : (System.Windows.Media.Brush)FindResource("Muted");
        RefreshNotesView();
        QueueSave();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedNote is null) return;
        Clipboard.SetText($"# {_selectedNote.Title}\n\n{_selectedNote.Content}");
        SavedLabel.Text = "Markdown copied";
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedNote is null) return;
        var dialog = new SaveFileDialog
        {
            Title = "Export note as Markdown",
            Filter = "Markdown files (*.md)|*.md|Text files (*.txt)|*.txt",
            FileName = $"{MakeSafeFileName(_selectedNote.Title)}.md",
            DefaultExt = ".md",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, $"# {_selectedNote.Title}\n\n{_selectedNote.Content}");
        SavedLabel.Text = "Markdown exported";
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Import a Markdown note", Filter = "Markdown and text files (*.md;*.markdown;*.txt)|*.md;*.markdown;*.txt|All files (*.*)|*.*", Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            Note? lastImported = null;
            foreach (var path in dialog.FileNames)
            {
                var content = await File.ReadAllTextAsync(path);
                var title = Path.GetFileNameWithoutExtension(path);
                var heading = System.Text.RegularExpressions.Regex.Match(content, @"^#\s+(.+?)(?:\r?\n|$)");
                if (heading.Success)
                {
                    title = heading.Groups[1].Value.Trim();
                    content = content[heading.Length..].TrimStart('\r', '\n');
                }
                var note = new Note { Title = title, Content = content, UpdatedAt = DateTime.Now };
                note.PropertyChanged += Note_PropertyChanged;
                _notes.Add(note);
                lastImported = note;
            }
            RefreshNotesView();
            if (lastImported is not null) NotesList.SelectedItem = lastImported;
            await SaveAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"The selected file couldn't be imported.\n\n{ex.Message}", "Import failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string MakeSafeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(title.Select(character => invalid.Contains(character) ? '_' : character));
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedNote is null) return;
        var answer = MessageBox.Show($"Delete “{_selectedNote.Title}”? This removes it from this device.", "Delete note", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        _selectedNote.PropertyChanged -= Note_PropertyChanged;
        _notes.Remove(_selectedNote);
        _selectedNote = null;
        NotesList.SelectedItem = null;
        SelectNote(null);
        RefreshNotesView();
        await SaveAsync();
    }

    private void UpdateWordCount()
    {
        var text = ContentBox.Text;
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var lines = text.Length == 0 ? 0 : text.Count(c => c == '\n') + 1;
        WordCount.Text = $"{words} words  ·  {lines} lines";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N) { NewNote_Click(sender, e); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S) { _saveTimer.Stop(); _ = SaveAsync(); e.Handled = true; }
    }
}
