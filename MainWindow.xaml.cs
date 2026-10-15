using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using Microsoft.Win32;
using Scrawl.Controls;
using Scrawl.Models;
using Scrawl.Services;

namespace Scrawl;

public partial class MainWindow : Window
{
    private sealed record Entry(string? Label, string? Keys = null, Action? Run = null, Func<bool>? Checked = null);

    private const int MaxPanes = 4;
    private const double SidebarWidth = 273;
    private const string DragFormat = "scrawl-note";

    private static readonly Dictionary<string, string> Extensions = new()
    {
        ["text"] = ".txt", ["csharp"] = ".cs", ["javascript"] = ".js", ["typescript"] = ".ts", ["python"] = ".py", ["java"] = ".java",
        ["cpp"] = ".cpp", ["go"] = ".go", ["rust"] = ".rs", ["json"] = ".json", ["html"] = ".html", ["css"] = ".css", ["sql"] = ".sql", ["shell"] = ".sh"
    };

    private readonly NoteStore _store = new();
    private readonly Settings _settings = Settings.Load();
    private readonly ObservableCollection<Note> _notes = [];
    private readonly List<NotePane> _panes = [];
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer _flyoutTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private Func<string?>? _dialogValidator;
    private ICollectionView? _notesView;
    private NotePane? _active;
    private FrameworkElement? _menuTarget;
    private TaskCompletionSource<bool>? _dialog;
    private bool _syncing;
    private bool _pressing;
    private Note? _dragNote;
    private Point _dragOrigin;

    public MainWindow()
    {
        Themes.Apply(Themes.Find(_settings.Theme));
        InitializeComponent();
        SidebarHost.Width = _settings.SidebarCollapsed ? 0 : SidebarWidth;

        _saveTimer.Tick += async (_, _) => { _saveTimer.Stop(); await SaveAsync(); };
        _flyoutTimer.Tick += FlyoutTick;
        Loaded += async (_, _) => await LoadAsync();
        Closing += (_, _) =>
        {
            _saveTimer.Stop();
            _settings.Save();
            if (_notesView is not null) _store.Save(_notes.ToList());
        };
    }

    /// <summary>Files passed on the command line, for example when a .txt file is opened with Scrawl.</summary>
    public string[] StartupFiles { get; set; } = [];

    /// <summary>Called when a second launch hands over files to this window.</summary>
    public void BringToFrontAndOpen(List<string> files)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (files.Count > 0 && _notesView is not null) _ = ImportPathsAsync(files);
    }

    private TextEditor? Ed => _active?.Editor;
    private Note? ActiveNote => _active?.Note;

    // ───────────── Loading & saving ─────────────

    private async Task LoadAsync()
    {
        try
        {
            var firstRun = !_store.Exists;
            foreach (var note in await _store.LoadAsync()) Add(note);
            if (firstRun) Add(WelcomeNote());
            _notesView = CollectionViewSource.GetDefaultView(_notes);
            _notesView.Filter = FilterNote;
            _notesView.SortDescriptions.Add(new SortDescription(nameof(Note.IsPinned), ListSortDirection.Descending));
            _notesView.SortDescriptions.Add(new SortDescription(nameof(Note.UpdatedAt), ListSortDirection.Descending));
            NotesList.ItemsSource = _notesView;
            RefreshNotesView();
            RefreshLayoutVisibility();
            if (firstRun) { OpenNote(_notes[0]); await SaveAsync(); }
            if (StartupFiles.Length > 0) await ImportPathsAsync(StartupFiles);
            if (!_settings.WelcomeDone) ShowWelcome();
        }
        catch (Exception ex)
        {
            await Info("Couldn't load notes", $"Scrawl couldn't read your notes.\n\n{ex.Message}");
        }
    }

    private static Note WelcomeNote() => new()
    {
        Title = "Welcome to Scrawl",
        IsPinned = true,
        Content = """
            Welcome to Scrawl.

            Every note is either Notepad or Code. Switch with the buttons above the editor.

              Notepad   plain text, exactly like a classic notepad
              Code      syntax colors like VS Code, line numbers, auto-indent

            Paste code into a Notepad note and Scrawl detects the language for you.

            Side by side
              Drag a note from the list onto the editor to open up to four notes at once.
              Right-click a note for more options, like locking it with a password.
              Hide the list with Ctrl+B. Hover the download button to export with any extension.

            Everything stays on this device. Export any note as .txt or .md from the File menu.
            """.Replace("\r\n", "\n")
    };

    private void Add(Note note)
    {
        note.PropertyChanged += Note_PropertyChanged;
        _notes.Add(note);
    }

    private bool FilterNote(object item)
    {
        if (item is not Note note) return false;
        var query = SearchBox.Text.Trim();
        return query.Length == 0 || note.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || note.Content.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshNotesView()
    {
        _notesView?.Refresh();
        NotesCount.Text = _notes.Count == 1 ? "1 note" : $"{_notes.Count} notes";
    }

    private async Task SaveAsync()
    {
        try
        {
            await _store.SaveAsync(_notes.ToList());
            SavedLabel.Text = "Saved";
        }
        catch (Exception ex)
        {
            SavedLabel.Text = "Save failed";
            await Info("Couldn't save", $"Your latest changes couldn't be saved.\n\n{ex.Message}");
        }
    }

    private void QueueSave()
    {
        SavedLabel.Text = "Saving…";
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void Note_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Note.IsPinned) or nameof(Note.UpdatedAt)) _notesView?.Refresh();
    }

    // ───────────── Panes (up to four notes side by side) ─────────────

    /// <summary>Cells for a given number of panes: 1 = full, 2 = side by side, 3 = two on top and one wide below, 4 = a 2x2 grid.</summary>
    private static (int Rows, int Cols, (int Row, int Col, int RowSpan, int ColSpan)[] Slots) SlotsFor(int count) => count switch
    {
        <= 1 => (1, 1, [(0, 0, 1, 1)]),
        2 => (1, 2, [(0, 0, 1, 1), (0, 1, 1, 1)]),
        3 => (2, 2, [(0, 0, 1, 1), (0, 1, 1, 1), (1, 0, 1, 2)]),
        _ => (2, 2, [(0, 0, 1, 1), (0, 1, 1, 1), (1, 0, 1, 1), (1, 1, 1, 1)])
    };

    private NotePane AddPane(Note note)
    {
        var pane = new NotePane { Settings = _settings };
        pane.Activated += SetActive;
        pane.CloseRequested += RemovePane;
        pane.Edited += _ => { QueueSave(); UpdateStats(); };
        pane.CaretMoved += p => { if (p == _active) UpdateCursor(); };
        pane.ZoomRequested += (_, delta) => Zoom(delta);
        pane.Pasted += OnPasting;
        pane.Unlocked += _ => { UpdateStats(); RefreshToolbar(); };
        pane.Load(note);
        _panes.Add(pane);
        ApplyLayout();
        SetActive(pane);
        return pane;
    }

    private void RemovePane(NotePane pane)
    {
        _panes.Remove(pane);
        PaneHost.Children.Remove(pane);
        if (_active == pane) _active = null;
        ApplyLayout();
        SetActive(_panes.LastOrDefault());
    }

    private const double Gap = 7;

    private void ApplyLayout()
    {
        PaneHost.Children.Clear();
        PaneHost.RowDefinitions.Clear();
        PaneHost.ColumnDefinitions.Clear();
        var count = _panes.Count;
        var star = new GridLength(1, GridUnitType.Star);
        var (rows, cols, slots) = SlotsFor(count);

        // Cells sit on even rows/columns, the draggable splitters on the odd ones in between.
        for (var c = 0; c < cols * 2 - 1; c++) PaneHost.ColumnDefinitions.Add(new ColumnDefinition { Width = c % 2 == 1 ? new GridLength(Gap) : star });
        for (var r = 0; r < rows * 2 - 1; r++) PaneHost.RowDefinitions.Add(new RowDefinition { Height = r % 2 == 1 ? new GridLength(Gap) : star });

        for (var i = 0; i < count; i++)
        {
            var pane = _panes[i];
            Grid.SetRow(pane, slots[i].Row * 2);
            Grid.SetColumn(pane, slots[i].Col * 2);
            Grid.SetRowSpan(pane, slots[i].RowSpan * 2 - 1);
            Grid.SetColumnSpan(pane, slots[i].ColSpan * 2 - 1);
            pane.SetCloseVisible(count > 1);
            PaneHost.Children.Add(pane);
        }

        if (cols == 2)
        {
            var vertical = new GridSplitter { Style = (Style)FindResource("SplitterV"), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, ResizeDirection = GridResizeDirection.Columns };
            Grid.SetColumn(vertical, 1);
            Grid.SetRowSpan(vertical, count == 3 ? 1 : rows * 2 - 1);
            PaneHost.Children.Add(vertical);
        }
        if (rows == 2)
        {
            var horizontal = new GridSplitter { Style = (Style)FindResource("SplitterH"), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, ResizeDirection = GridResizeDirection.Rows };
            Grid.SetRow(horizontal, 1);
            Grid.SetColumnSpan(horizontal, cols * 2 - 1);
            PaneHost.Children.Add(horizontal);
        }
        RefreshLayoutVisibility();
    }

    private void RefreshLayoutVisibility()
    {
        var any = _panes.Count > 0;
        EditorLayout.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetActive(NotePane? pane)
    {
        _active = pane;
        foreach (var p in _panes) p.SetActive(p == pane, _panes.Count > 1);
        _syncing = true;
        NotesList.SelectedItem = pane?.Note;
        _syncing = false;
        RefreshToolbar();
        UpdateStats();
        UpdateCursor();
    }

    /// <summary>Shows a note: focuses the pane that already has it, otherwise loads it into the active pane.</summary>
    private void OpenNote(Note note)
    {
        var existing = _panes.FirstOrDefault(p => p.Note == note);
        if (existing is not null) { SetActive(existing); return; }
        if (_active is null) { AddPane(note); return; }
        _active.Load(note);
        SetActive(_active);
    }

    private void OpenSideBySide(Note note)
    {
        var existing = _panes.FirstOrDefault(p => p.Note == note);
        if (existing is not null) SetActive(existing);
        else if (_panes.Count < MaxPanes) AddPane(note);
        else OpenNote(note);
    }

    private void ApplyAllPanes()
    {
        foreach (var pane in _panes) pane.ApplyOptions();
        RefreshToolbar();
    }

    // ───────────── Note list: selection, drag & drop, context menu ─────────────

    private void NotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _pressing) return;
        if (NotesList.SelectedItem is Note note) OpenNote(note);
    }

    private Note? NoteAt(DependencyObject? source) =>
        (ItemsControl.ContainerFromElement(NotesList, source) as ListBoxItem)?.DataContext as Note;

    private void NotesList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragNote = NoteAt(e.OriginalSource as DependencyObject);
        _dragOrigin = e.GetPosition(null);
        _pressing = _dragNote is not null;
        if (_dragNote is null) return;
        NotesList.PreviewMouseLeftButtonUp -= NotesList_MouseUp;
        NotesList.PreviewMouseLeftButtonUp += NotesList_MouseUp;
    }

    private void NotesList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        NotesList.PreviewMouseLeftButtonUp -= NotesList_MouseUp;
        var note = _dragNote;
        _pressing = false;
        _dragNote = null;
        if (note is not null) { NotesList.SelectedItem = note; OpenNote(note); }
    }

    private void NotesList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragNote is null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { _dragNote = null; _pressing = false; return; }
        var delta = e.GetPosition(null) - _dragOrigin;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var note = _dragNote;
        _dragNote = null;
        NotesList.PreviewMouseLeftButtonUp -= NotesList_MouseUp;
        if (_panes.Count > 0) ShowDropTiles();
        try { DragDrop.DoDragDrop(NotesList, new DataObject(DragFormat, note.Id.ToString()), DragDropEffects.Move); }
        finally
        {
            DropLayer.Visibility = Visibility.Collapsed;
            _pressing = false;
        }
    }

    private void ShowDropTiles()
    {
        var count = _panes.Count;
        var total = count >= MaxPanes ? count : count + 1;
        var (rows, cols, slots) = SlotsFor(total);
        DropLayer.Children.Clear();
        DropLayer.RowDefinitions.Clear();
        DropLayer.ColumnDefinitions.Clear();
        for (var r = 0; r < rows; r++) DropLayer.RowDefinitions.Add(new RowDefinition());
        for (var c = 0; c < cols; c++) DropLayer.ColumnDefinitions.Add(new ColumnDefinition());

        var accent = ((SolidColorBrush)Themes.Get("Accent")).Color;
        var idle = new SolidColorBrush(Color.FromArgb(46, accent.R, accent.G, accent.B));
        var hover = new SolidColorBrush(Color.FromArgb(96, accent.R, accent.G, accent.B));
        for (var i = 0; i < total; i++)
        {
            var index = i;
            var tile = new Border
            {
                Margin = new Thickness(10), CornerRadius = new CornerRadius(16), AllowDrop = true, Background = idle,
                BorderBrush = Themes.Get("Accent"), BorderThickness = new Thickness(2),
                Child = new TextBlock
                {
                    Text = i < count ? "Replace this note" : "Open side by side", FontSize = 15, FontWeight = FontWeights.SemiBold,
                    Foreground = Themes.Get("Accent"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetRow(tile, slots[i].Row);
            Grid.SetColumn(tile, slots[i].Col);
            Grid.SetRowSpan(tile, slots[i].RowSpan);
            Grid.SetColumnSpan(tile, slots[i].ColSpan);
            tile.DragEnter += (_, _) => tile.Background = hover;
            tile.DragLeave += (_, _) => tile.Background = idle;
            tile.DragOver += (_, e) => { e.Effects = DragDropEffects.Move; e.Handled = true; };
            tile.Drop += (_, e) => { e.Handled = true; DropNote(index, e.Data); };
            DropLayer.Children.Add(tile);
        }
        DropLayer.Visibility = Visibility.Visible;
    }

    private void DropNote(int slot, IDataObject data)
    {
        if (data.GetData(DragFormat) is not string id || !Guid.TryParse(id, out var guid)) return;
        var note = _notes.FirstOrDefault(n => n.Id == guid);
        if (note is null) return;
        var existing = _panes.FirstOrDefault(p => p.Note == note);

        if (slot < _panes.Count)
        {
            var target = _panes[slot];
            if (existing == target) { SetActive(target); return; }
            if (existing is not null) { _panes.Remove(existing); PaneHost.Children.Remove(existing); }
            target.Load(note);
            ApplyLayout();
            SetActive(target);
        }
        else if (existing is not null) SetActive(existing);
        else AddPane(note);
    }

    /// <summary>Right-clicking must open the menu without selecting (and so opening) the note.</summary>
    private void NotesList_RightDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void NotesList_RightClick(object sender, MouseButtonEventArgs e)
    {
        var note = NoteAt(e.OriginalSource as DependencyObject);
        var entries = new List<Entry>();
        if (note is null)
        {
            entries.Add(new("New note", "Ctrl+N", NewNote));
            entries.Add(new("Import…", "Ctrl+O", () => _ = ImportAsync()));
            entries.Add(new(null));
            entries.Add(new("Export all notes to folder…", null, ExportAll));
        }
        else
        {
            entries.Add(new("Open", null, () => OpenNote(note)));
            if (_panes.Count < MaxPanes && _panes.All(p => p.Note != note)) entries.Add(new("Open side by side", null, () => OpenSideBySide(note)));
            entries.Add(new(null));
            entries.Add(new(note.IsPinned ? "Unpin" : "Pin to top", null, () => TogglePin(note)));
            if (Readable(note))
            {
                entries.Add(new("Rename", null, () => { OpenNote(note); _active?.FocusTitle(); }));
                entries.Add(new("Duplicate", null, () => DuplicateNote(note)));
                entries.Add(new(null));
                entries.Add(new("Copy text", null, () => CopyNote(note)));
                entries.Add(new("Download as .txt", null, () => QuickExport(note, ".txt")));
                entries.Add(new("Export as .md…", null, () => ExportNote(note, ".md")));
            }
            entries.Add(new(null));
            entries.AddRange(LockEntries(note));
            entries.Add(new(null));
            entries.Add(new("Delete…", null, () => _ = DeleteNoteAsync(note)));
        }
        ShowMenu(NotesList, entries, PlacementMode.MousePoint);
        e.Handled = true;
    }

    private void NewNote_Click(object sender, RoutedEventArgs e) => NewNote();

    private void NewNote()
    {
        if (_notesView is null) return;
        SearchBox.Clear();
        var note = new Note { Title = "Untitled note", UpdatedAt = DateTime.Now, Language = _settings.NewNoteKind == "code" ? _settings.LastCode : "text" };
        Add(note);
        RefreshNotesView();
        OpenNote(note);
        _active?.FocusTitle();
        QueueSave();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _notesView?.Refresh();
    }

    private void Find_Click(object sender, RoutedEventArgs e)
    {
        SetSidebar(true);
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    // ───────────── Sidebar ─────────────

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e) => SetSidebar(_settings.SidebarCollapsed);

    /// <summary>Slides the notes list in or out.</summary>
    private void SetSidebar(bool visible)
    {
        if (_settings.SidebarCollapsed != visible) return;
        _settings.SidebarCollapsed = !visible;
        var target = visible ? SidebarWidth : 0;
        var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        animation.Completed += (_, _) => { SidebarHost.BeginAnimation(WidthProperty, null); SidebarHost.Width = target; };
        SidebarHost.BeginAnimation(WidthProperty, animation);
    }

    // ───────────── Language & mode ─────────────

    private string CurrentLanguage => ActiveNote?.Language ?? "text";

    private void Kind_Click(object sender, RoutedEventArgs e)
    {
        var tag = (string)((RadioButton)sender).Tag;
        SetLanguage(tag == "code" ? CodeHighlighter.Guess(Ed?.Text ?? "") ?? _settings.LastCode : "text");
    }

    private void SetLanguage(string language)
    {
        if (ActiveNote is null || _active is null) return;
        ActiveNote.Language = language;
        if (language != "text") _settings.LastCode = language;
        _active.ApplyOptions();
        RefreshToolbar();
        QueueSave();
    }

    private void RefreshToolbar()
    {
        var language = CurrentLanguage;
        ModeText.IsChecked = language == "text";
        ModeCode.IsChecked = language != "text";
        LangButton.Content = CodeHighlighter.NameOf(language) + "  ▾";
        UpdatePinIcon();
    }

    private void OnPasting(NotePane pane, string pasted)
    {
        if (pane.Note is not { Language: "text" } || pasted.Length < 40 || !pasted.Contains('\n')) return;
        if (CodeHighlighter.Guess(pasted) is not { } guess) return;
        SetLanguage(guess);
        SavedLabel.Text = $"Detected {CodeHighlighter.NameOf(guess)}";
    }

    private void Zoom(double delta, bool reset = false)
    {
        _settings.FontSize = reset ? 14 : Math.Clamp(_settings.FontSize + delta, 10, 32);
        ApplyAllPanes();
    }

    // ───────────── Toolbar & text tools ─────────────

    private void Undo_Click(object sender, RoutedEventArgs e) { Ed?.Undo(); Ed?.Focus(); }
    private void Redo_Click(object sender, RoutedEventArgs e) { Ed?.Redo(); Ed?.Focus(); }
    private void Cut_Click(object sender, RoutedEventArgs e) { Ed?.Cut(); Ed?.Focus(); }
    private void CopySel_Click(object sender, RoutedEventArgs e) { Ed?.Copy(); Ed?.Focus(); }
    private void Paste_Click(object sender, RoutedEventArgs e) { Ed?.Paste(); Ed?.Focus(); }
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => Zoom(1);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => Zoom(-1);

    private static readonly string[] LineBreaks = ["\r\n", "\n"];

    private static string[] SplitLines(string text) => text.Split(LineBreaks, StringSplitOptions.None);

    /// <summary>Applies a text transform to the selection, or to the whole note when nothing is selected.</summary>
    private void EditText(Func<string, string> transform)
    {
        if (Ed is not { } editor) return;
        if (editor.SelectionLength > 0) editor.SelectedText = transform(editor.SelectedText);
        else editor.Document.Replace(0, editor.Document.TextLength, transform(editor.Text));
        editor.Focus();
    }

    private void DuplicateLine()
    {
        if (Ed is not { } editor) return;
        var line = editor.Document.GetLineByOffset(editor.CaretOffset);
        editor.Document.Insert(line.EndOffset, "\n" + editor.Document.GetText(line));
    }

    private void DeleteLine()
    {
        if (Ed is not { } editor) return;
        var line = editor.Document.GetLineByOffset(editor.CaretOffset);
        editor.Document.Remove(line.Offset, line.TotalLength);
    }

    // ───────────── Note actions ─────────────

    /// <summary>True when the note's text is available (no password, or already unlocked).</summary>
    private static bool Readable(Note note) => !note.IsLocked || note.IsUnlocked;

    private List<Entry> LockEntries(Note note)
    {
        if (!note.IsLocked) return [new("Lock with password…", null, () => _ = LockNoteAsync(note))];
        var entries = new List<Entry>();
        if (note.IsUnlocked) entries.Add(new("Lock now", null, () => RelockNote(note)));
        else entries.Add(new("Unlock…", null, () => OpenNote(note)));
        entries.Add(new("Remove password…", null, () => _ = RemovePasswordAsync(note)));
        return entries;
    }

    private void RefreshPanesFor(Note note)
    {
        foreach (var pane in _panes.Where(p => p.Note == note)) pane.Load(note);
        RefreshToolbar();
        UpdateStats();
    }

    private async Task LockNoteAsync(Note note)
    {
        var password = await PromptPasswordAsync("Lock note", $"Choose a password for “{note.Title}”. You'll need it to open or delete the note. If you forget it, the note can't be recovered.", "Lock note", confirm: true);
        if (password is null) return;
        NoteLock.Lock(note, password);
        RefreshPanesFor(note);
        await SaveAsync();
    }

    private void RelockNote(Note note)
    {
        NoteLock.Relock(note);
        RefreshPanesFor(note);
        QueueSave();
    }

    private async Task RemovePasswordAsync(Note note)
    {
        var password = await PromptPasswordAsync("Remove password", $"Enter the password of “{note.Title}” to remove its protection.", "Remove", confirm: false, check: pw => NoteLock.Verify(note, pw));
        if (password is null) return;
        if (!note.IsUnlocked) NoteLock.TryUnlock(note, password);
        NoteLock.Remove(note);
        RefreshPanesFor(note);
        await SaveAsync();
    }

    private void Pin_Click(object sender, RoutedEventArgs e) { if (ActiveNote is { } note) TogglePin(note); }

    private void TogglePin(Note note)
    {
        note.IsPinned = !note.IsPinned;
        UpdatePinIcon();
        QueueSave();
    }

    private void UpdatePinIcon()
    {
        var pinned = ActiveNote?.IsPinned == true;
        PinIcon.Text = pinned ? "" : "";
        if (pinned) PinIcon.Foreground = Themes.Get("Accent"); else PinIcon.ClearValue(TextBlock.ForegroundProperty);
        PinButton.ToolTip = pinned ? "Unpin note" : "Pin note";
    }

    private void DuplicateNote(Note source)
    {
        if (!Readable(source)) return;
        var copy = new Note { Title = source.Title + " (copy)", Content = source.Content, Language = source.Language, UpdatedAt = DateTime.Now };
        Add(copy);
        RefreshNotesView();
        OpenNote(copy);
        QueueSave();
    }

    private static string ToFileText(Note note) => note.Content.Replace("\r\n", "\n").Replace("\n", "\r\n");

    private void Copy_Click(object sender, RoutedEventArgs e) { if (ActiveNote is { } note) CopyNote(note); }

    private void CopyNote(Note note)
    {
        if (!Readable(note)) { _ = Info("Note is locked", "Unlock the note first."); return; }
        Clipboard.SetText(ToFileText(note));
        SavedLabel.Text = "Copied";
    }

    private void ExportActive(string extension) { if (ActiveNote is { } note) ExportNote(note, extension); }

    private static string NormalizeExtension(string? text)
    {
        var cleaned = new string((text ?? string.Empty).Trim().Where(c => !Path.GetInvalidFileNameChars().Contains(c) && c != ' ').ToArray()).TrimStart('.');
        return cleaned.Length == 0 ? ".txt" : "." + cleaned;
    }

    /// <summary>Saves the note straight into the Downloads folder, no dialog.</summary>
    private void QuickExport(Note note, string? extension)
    {
        if (!Readable(note)) { _ = Info("Note is locked", "Unlock the note first."); return; }
        try
        {
            extension = NormalizeExtension(extension);
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(folder);
            var name = MakeSafeFileName(note.Title);
            var path = Path.Combine(folder, name + extension);
            for (var n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"{name} ({n}){extension}");
            File.WriteAllText(path, ToFileText(note));
            SavedLabel.Text = $"Saved to Downloads: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _ = Info("Export failed", ex.Message);
        }
    }

    private void ExportNote(Note note, string extension)
    {
        if (!Readable(note)) { _ = Info("Note is locked", "Unlock the note first."); return; }
        extension = NormalizeExtension(extension);
        var dialog = new SaveFileDialog
        {
            Title = "Export note",
            Filter = $"{extension.TrimStart('.').ToUpperInvariant()} file (*{extension})|*{extension}|All files (*.*)|*.*",
            FileName = MakeSafeFileName(note.Title) + extension,
            DefaultExt = extension,
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, ToFileText(note));
        SavedLabel.Text = "Exported";
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        ExportFlyout.IsOpen = false;
        if (ActiveNote is { } note) QuickExport(note, ".txt");
    }

    private void ExportButton_MouseEnter(object sender, MouseEventArgs e) => _flyoutTimer.Start();

    /// <summary>Opens the "export as" flyout while hovering the download button and closes it when the mouse leaves.</summary>
    private void FlyoutTick(object? sender, EventArgs e)
    {
        if (!ExportFlyout.IsOpen)
        {
            if (ExportButton.IsMouseOver && ActiveNote is not null) { ExtBox.Text = ".txt"; ExportFlyout.IsOpen = true; }
            else _flyoutTimer.Stop();
        }
        else if (!ExportButton.IsMouseOver && !FlyoutBorder.IsMouseOver && !ExtBox.IsKeyboardFocusWithin)
        {
            ExportFlyout.IsOpen = false;
            _flyoutTimer.Stop();
        }
    }

    private void ExportCustom_Click(object sender, RoutedEventArgs e)
    {
        ExportFlyout.IsOpen = false;
        if (ActiveNote is { } note) QuickExport(note, ExtBox.Text);
    }

    private void ExtBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ExportCustom_Click(sender, e);
        e.Handled = true;
    }

    private void ExportChoose_Click(object sender, RoutedEventArgs e)
    {
        ExportFlyout.IsOpen = false;
        if (ActiveNote is { } note) ExportNote(note, ExtBox.Text);
    }

    private async void ExportAll()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder for all notes" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var exported = 0;
            foreach (var note in _notes.Where(Readable))
            {
                exported++;
                var extension = Extensions.GetValueOrDefault(note.Language, ".txt");
                var name = MakeSafeFileName(note.Title);
                var path = Path.Combine(dialog.FolderName, name + extension);
                for (var n = 2; !used.Add(path) || File.Exists(path); n++) path = Path.Combine(dialog.FolderName, $"{name} ({n}){extension}");
                await File.WriteAllTextAsync(path, ToFileText(note));
            }
            SavedLabel.Text = $"Exported {exported} notes (locked notes are skipped)";
        }
        catch (Exception ex)
        {
            await Info("Export failed", ex.Message);
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e) => await ImportAsync();

    private async Task ImportAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import files",
            Filter = "Text and code|*.txt;*.md;*.cs;*.js;*.ts;*.py;*.java;*.cpp;*.c;*.go;*.rs;*.json;*.html;*.css;*.sql;*.sh;*.ps1|All files (*.*)|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;
        await ImportPathsAsync(dialog.FileNames);
    }

    private async Task ImportPathsAsync(IEnumerable<string> paths)
    {
        try
        {
            Note? last = null;
            foreach (var path in paths)
            {
                if (!File.Exists(path)) continue;
                var extension = Path.GetExtension(path).ToLowerInvariant();
                var language = extension switch
                {
                    ".c" or ".h" or ".hpp" => "cpp", ".ps1" => "shell", ".htm" or ".xml" => "html", ".md" or ".log" => "text",
                    _ => Extensions.FirstOrDefault(p => p.Value == extension).Key ?? "text"
                };
                var content = (await File.ReadAllTextAsync(path)).Replace("\r\n", "\n");
                last = new Note { Title = Path.GetFileNameWithoutExtension(path), Content = content, Language = language, UpdatedAt = DateTime.Now };
                Add(last);
            }
            RefreshNotesView();
            if (last is not null) OpenNote(last);
            await SaveAsync();
        }
        catch (Exception ex)
        {
            await Info("Import failed", $"The file couldn't be imported.\n\n{ex.Message}");
        }
    }

    private static string MakeSafeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(title.Select(c => invalid.Contains(c) ? '_' : c));
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveNote is { } note) await DeleteNoteAsync(note);
    }

    private async Task DeleteNoteAsync(Note note)
    {
        if (note.IsLocked)
        {
            if (await PromptPasswordAsync("Delete locked note?", $"Enter the password of “{note.Title}” to delete it. This can't be undone.", "Delete", confirm: false, check: pw => NoteLock.Verify(note, pw), danger: true) is null) return;
        }
        else if (!await ConfirmAsync("Delete note?", $"“{note.Title}” will be deleted from this device. This can't be undone.", "Delete", danger: true)) return;
        note.PropertyChanged -= Note_PropertyChanged;
        foreach (var pane in _panes.Where(p => p.Note == note).ToList()) RemovePane(pane);
        _notes.Remove(note);
        RefreshNotesView();
        await SaveAsync();
    }

    // ───────────── Settings & welcome ─────────────

    private bool _loadingSettingsUi;

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        _loadingSettingsUi = true;
        ThemeChoice.Content = _settings.Theme + "  ▾";
        FontSizeText.Text = _settings.FontSize.ToString("0");
        SetSans.IsChecked = _settings.SansText;
        SetWrap.IsChecked = _settings.WordWrap;
        SetNumbersText.IsChecked = _settings.LineNumbersText;
        SetNumbersCode.IsChecked = _settings.LineNumbersCode;
        SetList.IsChecked = !_settings.SidebarCollapsed;
        NewKindText.IsChecked = _settings.NewNoteKind != "code";
        NewKindCode.IsChecked = _settings.NewNoteKind == "code";
        _loadingSettingsUi = false;
        SettingsLayer.Visibility = Visibility.Visible;
    }

    private void CloseSettings()
    {
        SettingsLayer.Visibility = Visibility.Collapsed;
        _settings.Save();
    }

    private void SettingsClose_Click(object sender, RoutedEventArgs e) => CloseSettings();
    private void SettingsScrim_MouseDown(object sender, MouseButtonEventArgs e) => CloseSettings();

    private void Setting_Click(object sender, RoutedEventArgs e)
    {
        if (_loadingSettingsUi) return;
        _settings.SansText = SetSans.IsChecked == true;
        _settings.WordWrap = SetWrap.IsChecked == true;
        _settings.LineNumbersText = SetNumbersText.IsChecked == true;
        _settings.LineNumbersCode = SetNumbersCode.IsChecked == true;
        SetSidebar(SetList.IsChecked == true);
        ApplyAllPanes();
    }

    private void NewKind_Click(object sender, RoutedEventArgs e) => _settings.NewNoteKind = (string)((RadioButton)sender).Tag;

    private void FontSmaller_Click(object sender, RoutedEventArgs e) { Zoom(-1); FontSizeText.Text = _settings.FontSize.ToString("0"); }
    private void FontBigger_Click(object sender, RoutedEventArgs e) { Zoom(1); FontSizeText.Text = _settings.FontSize.ToString("0"); }

    private void ThemeChoice_Click(object sender, RoutedEventArgs e) =>
        ShowMenu(ThemeChoice, Themes.All.Select(t => new Entry(t.Name, null, () => { SetTheme(t); ThemeChoice.Content = t.Name + "  ▾"; }, () => t.Name == _settings.Theme)).ToList(), hOffset: -110);

    private void Shortcut_Click(object sender, RoutedEventArgs e) => CreateShortcut();

    private async void DefaultApp_Click(object sender, RoutedEventArgs e) => await SetUpTextFilesAsync();

    private void WelcomeAgain_Click(object sender, RoutedEventArgs e)
    {
        CloseSettings();
        ShowWelcome();
    }

    private void CreateShortcut()
    {
        try
        {
            SystemIntegration.CreateDesktopShortcut();
            SavedLabel.Text = "Desktop shortcut created";
        }
        catch (Exception ex)
        {
            _ = Info("Couldn't create the shortcut", ex.Message);
        }
    }

    /// <summary>Windows won't let an app make itself the default, so register it, then send the user to the page where they confirm.</summary>
    private async Task SetUpTextFilesAsync()
    {
        await Info("One more step in Windows", "Scrawl is now offered as an app for .txt files. Windows will open its Default apps page: search for \".txt\" and pick Scrawl there.");
        try { SystemIntegration.RegisterAsTextEditor(); }
        catch (Exception ex) { await Info("Couldn't set this up", ex.Message); }
    }

    private void ShowWelcome()
    {
        WelcomeShortcut.IsChecked = true;
        WelcomeDefault.IsChecked = false;
        WelcomeLayer.Visibility = Visibility.Visible;
    }

    private void WelcomeDone_Click(object sender, RoutedEventArgs e) => FinishWelcome(applyChoices: true);

    private void FinishWelcome(bool applyChoices)
    {
        var shortcut = applyChoices && WelcomeShortcut.IsChecked == true;
        var defaultApp = applyChoices && WelcomeDefault.IsChecked == true;
        WelcomeLayer.Visibility = Visibility.Collapsed;
        _settings.WelcomeDone = true;
        _settings.Save();
        if (shortcut) CreateShortcut();
        if (defaultApp) _ = SetUpTextFilesAsync();
    }

    // ───────────── In-app dialog ─────────────

    private Task<bool> ConfirmAsync(string title, string message, string ok, bool danger = false, bool showCancel = true, Func<string?>? validate = null, int passwordBoxes = 0)
    {
        _dialog?.TrySetResult(false);
        _dialog = new TaskCompletionSource<bool>();
        _dialogValidator = validate;
        DialogTitle.Text = title;
        DialogText.Text = message;
        DialogOk.Content = ok;
        DialogOk.Background = danger ? new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)) : Themes.Get("Accent");
        DialogOk.Foreground = danger ? Brushes.White : Themes.Get("AccentText");
        DialogCancel.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
        DialogError.Visibility = Visibility.Collapsed;
        DialogPw1.Clear();
        DialogPw2.Clear();
        DialogPw1.Visibility = passwordBoxes >= 1 ? Visibility.Visible : Visibility.Collapsed;
        DialogPw2.Visibility = passwordBoxes >= 2 ? Visibility.Visible : Visibility.Collapsed;
        DialogLayer.Visibility = Visibility.Visible;
        if (passwordBoxes > 0) Dispatcher.BeginInvoke(() => DialogPw1.Focus(), DispatcherPriority.Input);
        return _dialog.Task;
    }

    private Task Info(string title, string message) => ConfirmAsync(title, message, "OK", showCancel: false);

    /// <summary>Asks for a password inside the app. Returns null when cancelled.</summary>
    private async Task<string?> PromptPasswordAsync(string title, string message, string ok, bool confirm, Func<string, bool>? check = null, bool danger = false)
    {
        string? result = null;
        var accepted = await ConfirmAsync(title, message + (confirm ? "\n\nType the password twice." : string.Empty), ok, danger, validate: () =>
        {
            var password = DialogPw1.Password;
            if (password.Length == 0) return "Enter a password.";
            if (confirm && password.Length < 4) return "Use at least 4 characters.";
            if (confirm && password != DialogPw2.Password) return "The two passwords don't match.";
            if (check is not null && !check(password)) return "Wrong password.";
            result = password;
            return null;
        }, passwordBoxes: confirm ? 2 : 1);
        return accepted ? result : null;
    }

    private void AcceptDialog()
    {
        if (_dialogValidator?.Invoke() is { } error)
        {
            DialogError.Text = error;
            DialogError.Visibility = Visibility.Visible;
            return;
        }
        CloseDialog(true);
    }

    private void CloseDialog(bool result)
    {
        DialogLayer.Visibility = Visibility.Collapsed;
        DialogPw1.Clear();
        DialogPw2.Clear();
        _dialogValidator = null;
        _dialog?.TrySetResult(result);
    }

    private void DialogOk_Click(object sender, RoutedEventArgs e) => AcceptDialog();
    private void DialogCancel_Click(object sender, RoutedEventArgs e) => CloseDialog(false);
    private void DialogScrim_MouseDown(object sender, MouseButtonEventArgs e) => CloseDialog(false);

    // ───────────── Menus ─────────────

    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        var target = (FrameworkElement)sender;
        if (MenuPopup.IsOpen && ReferenceEquals(_menuTarget, target)) { MenuPopup.IsOpen = false; return; }
        OpenTopMenu(target);
    }

    private void Menu_MouseEnter(object sender, MouseEventArgs e)
    {
        var target = (FrameworkElement)sender;
        if (MenuPopup.IsOpen && !ReferenceEquals(_menuTarget, target) && (target == MenuFile || target == MenuEdit || target == MenuView)) OpenTopMenu(target);
    }

    private void OpenTopMenu(FrameworkElement target) =>
        ShowMenu(target, target == MenuFile ? FileMenu() : target == MenuEdit ? EditMenu() : ViewMenu());

    private List<Entry> FileMenu() =>
    [
        new("New note", "Ctrl+N", NewNote),
        new("Import…", "Ctrl+O", () => _ = ImportAsync()),
        new(null),
        new("Save", "Ctrl+S", () => { _saveTimer.Stop(); _ = SaveAsync(); }),
        new("Export as .txt…", "Ctrl+Shift+S", () => ExportActive(".txt")),
        new("Export as .md…", null, () => ExportActive(".md")),
        new("Export all notes to folder…", null, ExportAll),
        new(null),
        new("Copy whole note", null, () => { if (ActiveNote is { } n) CopyNote(n); }),
        new("Duplicate note", null, () => { if (ActiveNote is { } n) DuplicateNote(n); }),
        new("Pin / unpin", null, () => { if (ActiveNote is { } n) TogglePin(n); }),
        new("Lock / unlock…", null, () => { if (ActiveNote is { } n) ShowMenu(MenuFile, LockEntries(n)); }),
        new("Delete note…", "Ctrl+Shift+Del", () => { if (ActiveNote is { } n) _ = DeleteNoteAsync(n); }),
        new(null),
        new("Settings…", "Ctrl+,", OpenSettings),
        new("Exit", "Alt+F4", Close),
    ];

    private List<Entry> EditMenu() =>
    [
        new("Undo", "Ctrl+Z", () => Ed?.Undo()),
        new("Redo", "Ctrl+Y", () => Ed?.Redo()),
        new(null),
        new("Cut", "Ctrl+X", () => Ed?.Cut()),
        new("Copy", "Ctrl+C", () => Ed?.Copy()),
        new("Paste", "Ctrl+V", () => Ed?.Paste()),
        new("Select all", "Ctrl+A", () => Ed?.SelectAll()),
        new(null),
        new("Search notes", "Ctrl+F", () => Find_Click(this, new RoutedEventArgs())),
        new("Insert date and time", "F5", InsertDate),
        new(null),
        new("Duplicate line", "Ctrl+D", DuplicateLine),
        new("Delete line", "Ctrl+Shift+K", DeleteLine),
        new("UPPERCASE", null, () => EditText(t => t.ToUpper())),
        new("lowercase", null, () => EditText(t => t.ToLower())),
        new("Sort lines A–Z", null, () => EditText(t => string.Join("\n", SplitLines(t).Order(StringComparer.CurrentCultureIgnoreCase)))),
        new("Remove duplicate lines", null, () => EditText(t => string.Join("\n", SplitLines(t).Distinct()))),
        new("Trim trailing spaces", null, () => EditText(t => string.Join("\n", SplitLines(t).Select(l => l.TrimEnd())))),
    ];

    private List<Entry> ViewMenu() =>
    [
        new("Notepad", null, () => SetLanguage("text"), () => CurrentLanguage == "text"),
        new("Code", null, () => SetLanguage(CodeHighlighter.Guess(Ed?.Text ?? "") ?? _settings.LastCode), () => CurrentLanguage != "text"),
        new(null),
        new("Show / hide notes list", "Ctrl+B", () => SetSidebar(_settings.SidebarCollapsed)),
        new("Close pane", "Ctrl+W", () => { if (_active is not null && _panes.Count > 1) RemovePane(_active); }),
        new(null),
        new("Line numbers", null, () => { if (CurrentLanguage == "text") _settings.LineNumbersText = !_settings.LineNumbersText; else _settings.LineNumbersCode = !_settings.LineNumbersCode; ApplyAllPanes(); },
            () => CurrentLanguage == "text" ? _settings.LineNumbersText : _settings.LineNumbersCode),
        new("Word wrap", null, () => { _settings.WordWrap = !_settings.WordWrap; ApplyAllPanes(); }, () => _settings.WordWrap),
        new("Proportional font in Notepad", null, () => { _settings.SansText = !_settings.SansText; ApplyAllPanes(); }, () => _settings.SansText),
        new(null),
        new("Zoom in", "Ctrl++", () => Zoom(1)),
        new("Zoom out", "Ctrl+-", () => Zoom(-1)),
        new("Reset zoom", "Ctrl+0", () => Zoom(0, reset: true)),
        new(null),
        new("Choose language…", null, () => Lang_Click(LangButton, new RoutedEventArgs())),
        new("Choose theme…", null, () => Theme_Click(ThemeButton, new RoutedEventArgs())),
    ];

    private void InsertDate()
    {
        if (Ed is { } editor) editor.SelectedText = DateTime.Now.ToString("HH:mm dd-MM-yyyy");
    }

    private void Lang_Click(object sender, RoutedEventArgs e)
    {
        var entries = new List<Entry> { new("Auto-detect", null, () => SetLanguage(CodeHighlighter.Guess(Ed?.Text ?? "") ?? "text")), new(null) };
        entries.AddRange(CodeHighlighter.Languages.Select(l => new Entry(l.Name, null, () => SetLanguage(l.Id), () => CurrentLanguage == l.Id)));
        ShowMenu(LangButton, entries, PlacementMode.Top, hOffset: -90);
    }

    private void Theme_Click(object sender, RoutedEventArgs e) =>
        ShowMenu(ThemeButton, Themes.All.Select(t => new Entry(t.Name, null, () => SetTheme(t), () => t.Name == _settings.Theme)).ToList(), hOffset: -200);

    private void ShowMenu(FrameworkElement target, List<Entry> entries, PlacementMode placement = PlacementMode.Bottom, double hOffset = 0)
    {
        MenuList.Children.Clear();
        foreach (var entry in entries)
        {
            if (entry.Label is null)
            {
                MenuList.Children.Add(new Border { Height = 1, Background = Themes.Get("Line"), Margin = new Thickness(8, 4, 8, 4) });
                continue;
            }
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (entry.Checked?.Invoke() == true)
                grid.Children.Add(new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 11, Foreground = Themes.Get("Accent"), VerticalAlignment = VerticalAlignment.Center });
            var label = new TextBlock { Text = entry.Label, FontSize = 13, Foreground = Themes.Get("Text"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);
            if (entry.Keys is not null)
            {
                var keys = new TextBlock { Text = entry.Keys, FontSize = 11.5, Foreground = Themes.Get("Muted"), Margin = new Thickness(28, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(keys, 2);
                grid.Children.Add(keys);
            }
            var item = new Button { Content = grid, Padding = new Thickness(8, 7, 10, 7), HorizontalContentAlignment = HorizontalAlignment.Stretch, Foreground = Themes.Get("Text") };
            var run = entry.Run;
            item.Click += (_, _) =>
            {
                MenuPopup.IsOpen = false;
                if (run is not null) Dispatcher.BeginInvoke(run, DispatcherPriority.Background);
            };
            MenuList.Children.Add(item);
        }
        _menuTarget = target;
        MenuPopup.IsOpen = false;
        MenuPopup.PlacementTarget = target;
        MenuPopup.Placement = placement;
        MenuPopup.HorizontalOffset = hOffset;
        MenuPopup.IsOpen = true;
    }

    // ───────────── Themes & window ─────────────

    private void SetTheme(Theme theme)
    {
        _settings.Theme = theme.Name;
        _settings.Save();
        Themes.Apply(theme);
        foreach (var pane in _panes) pane.ApplyTheme();
        UpdatePinIcon();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        var radius = maximized ? new CornerRadius(0) : new CornerRadius(16);
        Root.Margin = ShadowLayer.Margin = new Thickness(maximized ? 7 : 10);
        Root.CornerRadius = ShadowLayer.CornerRadius = radius;
        ShadowLayer.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        UpdateClip();
    }

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateClip();

    /// <summary>Clips the content to the rounded corners of the window.</summary>
    private void UpdateClip()
    {
        var radius = WindowState == WindowState.Maximized ? 0 : 16;
        Root.Clip = new RectangleGeometry(new Rect(0, 0, Root.ActualWidth, Root.ActualHeight), radius, radius);
    }

    // ───────────── Status & shortcuts ─────────────

    private void UpdateStats()
    {
        if (_active?.IsShowingLock == true) { WordCount.Text = "Locked"; return; }
        var text = Ed?.Text ?? string.Empty;
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        WordCount.Text = $"{words} words  ·  {text.Length} characters";
    }

    private void UpdateCursor()
    {
        if (Ed is not { } editor) { CursorLabel.Text = string.Empty; return; }
        var caret = editor.TextArea.Caret;
        CursorLabel.Text = $"Ln {caret.Line}, Col {caret.Column}";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DialogLayer.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape) { CloseDialog(false); e.Handled = true; }
            else if (e.Key == Key.Enter) { AcceptDialog(); e.Handled = true; }
            return;
        }
        if (WelcomeLayer.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape) { FinishWelcome(applyChoices: false); e.Handled = true; }
            return;
        }
        if (SettingsLayer.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape) { CloseSettings(); e.Handled = true; }
            return;
        }
        var mods = Keyboard.Modifiers;
        var ctrl = mods == ModifierKeys.Control;
        var ctrlShift = mods == (ModifierKeys.Control | ModifierKeys.Shift);
        var inEditor = Ed?.TextArea.IsKeyboardFocusWithin == true;
        if (ctrl && e.Key == Key.OemComma) OpenSettings();
        else if (ctrl && e.Key == Key.N) NewNote();
        else if (ctrl && e.Key == Key.O) _ = ImportAsync();
        else if (ctrl && e.Key == Key.F) Find_Click(sender, e);
        else if (ctrl && e.Key == Key.S) { _saveTimer.Stop(); _ = SaveAsync(); }
        else if (ctrlShift && e.Key == Key.S) ExportActive(".txt");
        else if (ctrlShift && e.Key == Key.Delete && ActiveNote is { } doomed) _ = DeleteNoteAsync(doomed);
        else if (ctrl && (e.Key == Key.B || e.Key is Key.Oem5 or Key.OemBackslash)) SetSidebar(_settings.SidebarCollapsed);
        else if (ctrl && e.Key == Key.W && _active is not null && _panes.Count > 1) RemovePane(_active);
        else if (ctrl && e.Key == Key.D && inEditor && CurrentLanguage != "text") DuplicateLine();
        else if (ctrlShift && e.Key == Key.K && inEditor) DeleteLine();
        else if (e.Key == Key.F5 && inEditor) InsertDate();
        else if (ctrl && e.Key is Key.OemPlus or Key.Add) Zoom(1);
        else if (ctrl && e.Key is Key.OemMinus or Key.Subtract) Zoom(-1);
        else if (ctrl && e.Key is Key.D0 or Key.NumPad0) Zoom(0, reset: true);
        else if (e.Key == Key.Escape && SearchBox.IsKeyboardFocused) { SearchBox.Clear(); NotesList.Focus(); }
        else return;
        e.Handled = true;
    }
}
