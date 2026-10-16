using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Scrawl.Controls;

/// <summary>
/// Makes a PasswordBox take a PIN: digits 0 to 9 only (typing and pasting), at most 4 or 6 of them.
/// Configure(box, 0) turns the box back into a normal password box.
/// </summary>
public static class PinInput
{
    private static readonly DependencyProperty LengthProperty = DependencyProperty.RegisterAttached("PinLength", typeof(int), typeof(PinInput), new PropertyMetadata(0));
    private static readonly DependencyProperty WiredProperty = DependencyProperty.RegisterAttached("PinWired", typeof(bool), typeof(PinInput), new PropertyMetadata(false));

    public static void Configure(PasswordBox box, int length)
    {
        box.SetValue(LengthProperty, length);
        box.MaxLength = length;
        if ((bool)box.GetValue(WiredProperty)) return;
        box.SetValue(WiredProperty, true);

        box.PreviewTextInput += (_, e) =>
        {
            if (IsPin(box) && !IsDigits(e.Text)) e.Handled = true;
        };
        box.PreviewKeyDown += (_, e) =>
        {
            if (IsPin(box) && e.Key == Key.Space) e.Handled = true;
        };
        DataObject.AddPastingHandler(box, (_, e) =>
        {
            if (!IsPin(box)) return;
            var text = e.DataObject.GetData(DataFormats.UnicodeText) as string;
            if (text is null || !IsDigits(text)) e.CancelCommand();
        });
    }

    private static bool IsPin(PasswordBox box) => (int)box.GetValue(LengthProperty) > 0;

    public static bool IsDigits(string text) => text.Length > 0 && text.All(c => c is >= '0' and <= '9');
}
