using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Scrawl.Services;

namespace Scrawl.Controls;

/// <summary>
/// A number pad for entering a PIN: dots show how many digits are typed, round keys 0 to 9 and a delete key.
/// The keyboard works too (digits, number pad, Backspace). <see cref="Completed"/> fires when the last digit is in.
/// </summary>
public partial class PinPad : UserControl
{
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
    private int _length = 4;
    private bool _error;

    public PinPad()
    {
        InitializeComponent();
        BuildDots();
    }

    /// <summary>Raised with the digits typed, as soon as there are <see cref="Length"/> of them.</summary>
    public event Action<PinPad, string>? Completed;

    public string Entered { get; private set; } = string.Empty;

    /// <summary>Number of digits of the PIN: 4 or 6.</summary>
    public int Length
    {
        get => _length;
        set
        {
            _length = value is 6 ? 6 : 4;
            Reset();
            BuildDots();
        }
    }

    public void Press(char digit)
    {
        if (digit is < '0' or > '9' || Entered.Length >= _length) return;
        ClearError();
        Entered += digit;
        UpdateDots();
        if (Entered.Length == _length)
        {
            var pin = Entered;
            // Let the last dot show up before whatever happens next (a check can take a moment).
            Dispatcher.BeginInvoke(() => Completed?.Invoke(this, pin), DispatcherPriority.Background);
        }
    }

    public void Backspace()
    {
        if (Entered.Length == 0) return;
        ClearError();
        Entered = Entered[..^1];
        UpdateDots();
    }

    /// <summary>Forgets the digits typed so far.</summary>
    public void Reset()
    {
        Entered = string.Empty;
        ClearError();
        UpdateDots();
    }

    /// <summary>Shakes and turns the dots red for a moment, then starts over.</summary>
    public void ShowError()
    {
        _error = true;
        UpdateDots();
        var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(360) };
        foreach (var (ms, x) in new[] { (0, 0.0), (60, -10.0), (120, 10.0), (180, -8.0), (240, 8.0), (300, -4.0), (360, 0.0) })
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(x, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
        Shake.BeginAnimation(TranslateTransform.XProperty, shake);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Reset();
        };
        timer.Start();
    }

    public void FocusPad() => Focus();

    private void ClearError()
    {
        if (!_error) return;
        _error = false;
        UpdateDots();
    }

    private void BuildDots()
    {
        Dots.Children.Clear();
        for (var i = 0; i < _length; i++) Dots.Children.Add(new Ellipse { Width = 13, Height = 13, Margin = new Thickness(8, 0, 8, 0), StrokeThickness = 1.5 });
        UpdateDots();
    }

    private void UpdateDots()
    {
        for (var i = 0; i < Dots.Children.Count; i++)
        {
            var dot = (Ellipse)Dots.Children[i];
            var filled = i < Entered.Length || _error;
            dot.Stroke = _error ? ErrorBrush : (TryFindResource("Muted") as Brush ?? Brushes.Gray);
            dot.Fill = !filled ? Brushes.Transparent : _error ? ErrorBrush : (TryFindResource("Accent") as Brush ?? Brushes.DodgerBlue);
            if (filled && !_error) dot.Stroke = (TryFindResource("Accent") as Brush ?? Brushes.DodgerBlue);
        }
    }

    private void Key_Click(object sender, RoutedEventArgs e)
    {
        Press(((string)((Button)sender).Tag)[0]);
        Focus();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        Backspace();
        Focus();
    }

    private void Pad_PreviewMouseDown(object sender, MouseButtonEventArgs e) => Focus();

    private void Pad_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var digit = e.Key is >= Key.D0 and <= Key.D9 && Keyboard.Modifiers == ModifierKeys.None ? (char)('0' + (e.Key - Key.D0))
            : e.Key is >= Key.NumPad0 and <= Key.NumPad9 ? (char)('0' + (e.Key - Key.NumPad0))
            : '\0';
        if (digit != '\0') { Press(digit); e.Handled = true; }
        else if (e.Key == Key.Back) { Backspace(); e.Handled = true; }
    }
}
