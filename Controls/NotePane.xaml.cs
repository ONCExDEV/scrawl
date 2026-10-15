using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Indentation;
using Scrawl.Models;
using Scrawl.Services;

namespace Scrawl.Controls;

/// <summary>One note on screen: a title and an editor. Notepad notes are plain; code notes get syntax colors.</summary>
public partial class NotePane : UserControl
{
    private static readonly FontFamily Mono = new("Cascadia Code, Consolas");
    private static readonly FontFamily Sans = new("Segoe UI Variable Text, Segoe UI");
    private readonly CodeColorizer _colorizer = new();
    private bool _loading;

    public NotePane()
    {
        InitializeComponent();
        Editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.TextChanged += (_, _) =>
        {
            if (_loading || Note is null || IsShowingLock) return;
            Note.Content = Editor.Text;
            Note.UpdatedAt = DateTime.Now;
            Edited?.Invoke(this);
        };
        Editor.TextArea.Caret.PositionChanged += (_, _) => CaretMoved?.Invoke(this);
        DataObject.AddPastingHandler(Editor.TextArea, (_, e) =>
        {
            if (e.DataObject.GetData(DataFormats.UnicodeText) is string text) Dispatcher.BeginInvoke(() => Pasted?.Invoke(this, text));
        });
    }

    public Note? Note { get; private set; }
    public Settings Settings { get; set; } = new();

    public event Action<NotePane>? Activated;
    public event Action<NotePane>? CloseRequested;
    public event Action<NotePane>? Edited;
    public event Action<NotePane>? CaretMoved;
    public event Action<NotePane>? Unlocked;
    public event Action<NotePane, int>? ZoomRequested;
    public event Action<NotePane, string>? Pasted;

    public bool IsShowingLock => LockLayer.Visibility == Visibility.Visible;
    public bool IsCode => (Note?.Language ?? "text") != "text";

    public void Load(Note note)
    {
        _loading = true;
        Note = note;
        TitleBox.Text = note.Title;
        var locked = note.IsLocked && !note.IsUnlocked;
        if (locked) ShowLock(); else HideLock();
        Editor.Document = new TextDocument(locked ? Decoy() : note.Content);
        Editor.ScrollToHome();
        _loading = false;
        ApplyOptions();
    }

    /// <summary>Blurred filler text, so a locked note never shows (or even holds) its real content.</summary>
    private static string Decoy()
    {
        var random = new Random(7);
        var text = new StringBuilder();
        for (var line = 0; line < 22; line++)
        {
            for (var word = 0; word < random.Next(4, 11); word++) text.Append(new string((char)('a' + random.Next(26)), random.Next(2, 9))).Append(' ');
            text.Append(Environment.NewLine);
        }
        return text.ToString();
    }

    private void ShowLock()
    {
        LockLayer.Visibility = Visibility.Visible;
        EditorHost.Effect = new BlurEffect { Radius = 14 };
        Editor.IsReadOnly = true;
        TitleBox.IsReadOnly = true;
        UnlockBox.Password = string.Empty;
        UnlockError.Visibility = Visibility.Collapsed;
        Dispatcher.BeginInvoke(() => UnlockBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void HideLock()
    {
        LockLayer.Visibility = Visibility.Collapsed;
        EditorHost.Effect = null;
        Editor.IsReadOnly = false;
        TitleBox.IsReadOnly = false;
    }

    private void Unlock_Click(object sender, RoutedEventArgs e) => TryUnlock();

    private void UnlockBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        TryUnlock();
        e.Handled = true;
    }

    private void TryUnlock()
    {
        if (Note is null) return;
        if (!NoteLock.TryUnlock(Note, UnlockBox.Password))
        {
            UnlockError.Visibility = Visibility.Visible;
            UnlockBox.Clear();
            UnlockBox.Focus();
            return;
        }
        Load(Note);
        Unlocked?.Invoke(this);
        FocusEditor();
    }

    public void SetActive(bool active, bool showBar)
    {
        ActiveBar.Visibility = active && showBar ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetCloseVisible(bool visible) => CloseButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Applies the note's language and the user's settings. Notepad mode is deliberately plain.</summary>
    public void ApplyOptions()
    {
        var language = Note?.Language ?? "text";
        var code = language != "text" && !IsShowingLock;
        var sans = !code && Settings.SansText;
        _colorizer.Language = IsShowingLock ? "text" : language;
        Editor.ShowLineNumbers = !IsShowingLock && (code ? Settings.LineNumbersCode : Settings.LineNumbersText);
        Editor.WordWrap = Settings.WordWrap;
        Editor.HorizontalScrollBarVisibility = Settings.WordWrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        Editor.FontFamily = sans ? Sans : Mono;
        Editor.FontSize = Settings.FontSize + (sans ? 1 : 0);
        Editor.Options.HighlightCurrentLine = code;
        Editor.Options.ConvertTabsToSpaces = code;
        Editor.Options.IndentationSize = 2;
        Editor.Options.CutCopyWholeLine = code;
        Editor.TextArea.IndentationStrategy = code ? new DefaultIndentationStrategy() : new NoIndentation();
        ApplyTheme();
        Editor.TextArea.TextView.Redraw();
    }

    public void ApplyTheme()
    {
        var area = Editor.TextArea;
        area.Caret.CaretBrush = Themes.Get("Text");
        area.SelectionBrush = Themes.Get("Sel");
        area.SelectionForeground = null;
        area.SelectionBorder = null;
        area.TextView.CurrentLineBackground = Themes.Get("CurLine");
        area.TextView.CurrentLineBorder = new Pen(Brushes.Transparent, 0);
        Editor.TextArea.TextView.Redraw();
    }

    public void FocusEditor() => Editor.Focus();
    public void FocusTitle() { TitleBox.Focus(); TitleBox.SelectAll(); }

    private void TitleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || Note is null) return;
        Note.Title = TitleBox.Text;
        Note.UpdatedAt = DateTime.Now;
        Edited?.Invoke(this);
    }

    private void TitleBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Editor.Focus();
        e.Handled = true;
    }

    private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        ZoomRequested?.Invoke(this, e.Delta > 0 ? 1 : -1);
        e.Handled = true;
    }

    private void Focused(object sender, KeyboardFocusChangedEventArgs e) => Activated?.Invoke(this);
    private void Pane_PreviewMouseDown(object sender, MouseButtonEventArgs e) => Activated?.Invoke(this);
    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this);
}
