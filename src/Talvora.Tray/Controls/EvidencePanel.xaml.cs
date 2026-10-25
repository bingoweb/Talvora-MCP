using System.Windows;
using Talvora.Shared;

namespace Talvora.Tray.Controls;

internal sealed partial class EvidencePanel : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(EvidencePanel),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty SafeTextProperty =
        DependencyProperty.Register(nameof(SafeText), typeof(string), typeof(EvidencePanel),
            new PropertyMetadata(string.Empty));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string SafeText
    {
        get => (string)GetValue(SafeTextProperty);
        private set => SetValue(SafeTextProperty, value);
    }

    public EvidencePanel() => InitializeComponent();

    private static void OnTextChanged(DependencyObject source, DependencyPropertyChangedEventArgs args)
    {
        var panel = (EvidencePanel)source;
        panel.SafeText = FileLog.RedactSensitiveData(args.NewValue as string);
    }

    private void OnCopy(object sender, RoutedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(SafeText)) return;
        try
        {
            System.Windows.Clipboard.SetText(SafeText);
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            TrayLog.Write("Control Center evidence clipboard unavailable", ex);
        }
        catch (InvalidOperationException ex)
        {
            TrayLog.Write("Control Center evidence clipboard is busy", ex);
        }
    }
}
