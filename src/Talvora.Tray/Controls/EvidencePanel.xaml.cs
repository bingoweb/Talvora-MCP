using System.Windows;
using Talvora.Shared;

namespace Talvora.Tray.Controls;

internal sealed partial class EvidencePanel : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(EvidencePanel),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public EvidencePanel() => InitializeComponent();

    private static void OnTextChanged(DependencyObject source, DependencyPropertyChangedEventArgs args)
    {
        var panel = (EvidencePanel)source;
        if (args.NewValue is string text)
        {
            var safe = FileLog.RedactSensitiveData(text);
            if (safe != text) panel.SetCurrentValue(TextProperty, safe);
        }
    }

    private void OnCopy(object sender, RoutedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(Text)) return;
        try
        {
            System.Windows.Clipboard.SetText(FileLog.RedactSensitiveData(Text));
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            TrayLog.Write("Control Center evidence clipboard unavailable", ex);
        }
    }
}
