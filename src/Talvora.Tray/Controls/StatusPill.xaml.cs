using System.Windows;
using Talvora.Tray.ViewModels;

namespace Talvora.Tray.Controls;

internal sealed partial class StatusPill : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty StateProperty =
        DependencyProperty.Register(nameof(State), typeof(ComponentVisualState),
            typeof(StatusPill), new PropertyMetadata(ComponentVisualState.Neutral, OnStateChanged));

    public static readonly DependencyProperty DisplayTextProperty =
        DependencyProperty.Register(nameof(DisplayText), typeof(string),
            typeof(StatusPill), new PropertyMetadata("○ İsteğe bağlı kapalı"));

    public ComponentVisualState State
    {
        get => (ComponentVisualState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string DisplayText
    {
        get => (string)GetValue(DisplayTextProperty);
        private set => SetValue(DisplayTextProperty, value);
    }

    public StatusPill()
    {
        InitializeComponent();
        UpdateText();
    }

    private static void OnStateChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((StatusPill)sender).UpdateText();

    private void UpdateText() => DisplayText = State switch
    {
        ComponentVisualState.Ready => "● Hazır",
        ComponentVisualState.Attention => "▲ Dikkat istiyor",
        ComponentVisualState.Offline => "✕ Ulaşılamıyor",
        ComponentVisualState.Repairing => "↻ Onarılıyor",
        _ => "○ İsteğe bağlı kapalı"
    };
}
