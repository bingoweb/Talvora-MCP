using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace Talvora.Tray.Controls;

public sealed class NavRailItem : System.Windows.Controls.RadioButton
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(NavRailItem), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty SymbolProperty =
        DependencyProperty.Register(nameof(Symbol), typeof(SymbolRegular), typeof(NavRailItem), new PropertyMetadata(SymbolRegular.Home24));
    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(NavRailItem), new PropertyMetadata(false));

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public SymbolRegular Symbol { get => (SymbolRegular)GetValue(SymbolProperty); set => SetValue(SymbolProperty, value); }
    public bool IsExpanded { get => (bool)GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }
}
