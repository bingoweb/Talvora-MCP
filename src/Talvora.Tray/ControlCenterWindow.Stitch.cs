using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Orientation = System.Windows.Controls.Orientation;
using HAlign = System.Windows.HorizontalAlignment;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using UiButton = Wpf.Ui.Controls.Button;

namespace Talvora.Tray;

// Native WPF translation of the Google Stitch "Kontrol Merkezi Desktop" direction.
// All navigation routes into existing live dashboard, lifecycle and event workflows.
internal sealed partial class ControlCenterWindow
{
    private UiButton _navOverviewButton = null!;
    private UiButton _navServicesButton = null!;
    private UiButton _navEventsButton = null!;
    private UniformGrid _overviewMetricsGrid = null!;
    private TextBlock _metricReady = null!;
    private TextBlock _metricInactive = null!;
    private TextBlock _metricAttention = null!;
    private TextBlock _metricTotal = null!;
    private TextBlock _sidebarHealthText = null!;
    private Border _sidebarHealthDot = null!;

    private UIElement BuildNavigationSidebar()
    {
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var brand = new StackPanel { Margin = new Thickness(0, 12, 0, 34) };
        var logoRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        logoRow.Children.Add(new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(13),
            Background = AccentBrush,
            Child = new TextBlock
            {
                Text = "T",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HAlign.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
        var brandCopy = new StackPanel
        {
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        brandCopy.Children.Add(new TextBlock
        {
            Text = "TALVORA",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
        });
        brandCopy.Children.Add(new TextBlock
        {
            Text = "CONTROL CENTER",
            Margin = new Thickness(0, 3, 0, 0),
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = SidebarSecondaryBrush,
        });
        logoRow.Children.Add(brandCopy);
        brand.Children.Add(logoRow);
        layout.Children.Add(brand);

        var navigation = new StackPanel();
        navigation.Children.Add(new TextBlock
        {
            Text = "ÇALIŞMA ALANI",
            Margin = new Thickness(12, 0, 0, 12),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = SidebarSecondaryBrush,
        });
        _navOverviewButton = CreateSidebarNavigation(
            "Genel Bakış",
            SymbolRegular.Home20,
            () =>
            {
                ShowDashboard();
                _searchBox.Text = string.Empty;
                _filterBox.SelectedIndex = 0;
                SelectNavigation(_navOverviewButton);
            });
        _navServicesButton = CreateSidebarNavigation(
            "MCP Hizmetleri",
            SymbolRegular.Server20,
            () =>
            {
                ShowDashboard();
                SelectNavigation(_navServicesButton);
                _cardsPanel.BringIntoView();
                _searchBox.Focus();
            });
        _navEventsButton = CreateSidebarNavigation(
            "Olaylar",
            SymbolRegular.History20,
            () =>
            {
                ShowDashboard();
                SelectNavigation(_navEventsButton);
                _eventsExpander.IsExpanded = true;
                _eventsExpander.BringIntoView();
            });
        navigation.Children.Add(_navOverviewButton);
        navigation.Children.Add(_navServicesButton);
        navigation.Children.Add(_navEventsButton);
        Grid.SetRow(navigation, 1);
        layout.Children.Add(navigation);

        var footer = new StackPanel
        {
            Margin = new Thickness(4, 12, 4, 12),
        };
        footer.Children.Add(new Border
        {
            Height = 1,
            Background = SidebarHoverBrush,
            Margin = new Thickness(0, 0, 0, 20),
        });
        footer.Children.Add(new TextBlock
        {
            Text = "YEREL SİSTEM",
            Foreground = SidebarSecondaryBrush,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 0, 0, 10),
        });

        var stateRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 0, 0, 0),
        };
        _sidebarHealthDot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = SidebarAccentBrush,
            Margin = new Thickness(0, 5, 10, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        stateRow.Children.Add(_sidebarHealthDot);
        _sidebarHealthText = new TextBlock
        {
            Text = "Kontrol ediliyor",
            FontSize = 12,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        stateRow.Children.Add(_sidebarHealthText);
        footer.Children.Add(stateRow);
        footer.Children.Add(new TextBlock
        {
            Text = "Bu Windows cihazı",
            Margin = new Thickness(26, 5, 0, 0),
            FontSize = 10.5,
            Foreground = SidebarSecondaryBrush,
        });

        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);

        var sidebar = new Border
        {
            Background = SidebarBrush,
            Padding = new Thickness(18, 14, 18, 10),
            Child = layout,
        };
        SelectNavigation(_navOverviewButton);
        return sidebar;
    }

    private UiButton CreateSidebarNavigation(
        string label,
        SymbolRegular icon,
        Action onClick)
    {
        var button = new UiButton
        {
            Content = label,
            Icon = new SymbolIcon { Symbol = icon },
            HorizontalAlignment = HAlign.Stretch,
            HorizontalContentAlignment = HAlign.Left,
            MinHeight = 47,
            Margin = new Thickness(0, 0, 0, 7),
            Style = FindStyle("TalvoraSidebarButtonStyle"),
        };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => onClick();
        return button;
    }

    private void SelectNavigation(UiButton selected)
    {
        foreach (var button in new[]
                 {
                     _navOverviewButton,
                     _navServicesButton,
                     _navEventsButton,
                 })
        {
            if (button is null)
            {
                continue;
            }

            var active = ReferenceEquals(button, selected);
            button.Background = active ? SidebarHoverBrush : SidebarBrush;
            button.Foreground = active ? Brushes.White : SidebarSecondaryBrush;
            button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private UIElement BuildOverviewMetrics()
    {
        _overviewMetricsGrid = new UniformGrid
        {
            Columns = 4,
            Margin = new Thickness(-6, 0, -6, 26),
        };

        _overviewMetricsGrid.Children.Add(
            CreateOverviewMetric("Hazır MCP", out _metricReady, ReadyBrush));
        _overviewMetricsGrid.Children.Add(
            CreateOverviewMetric("İsteğe bağlı kapalı", out _metricInactive, IdleBrush));
        _overviewMetricsGrid.Children.Add(
            CreateOverviewMetric("Dikkat gerektiren", out _metricAttention, AttentionBrush));
        _overviewMetricsGrid.Children.Add(
            CreateOverviewMetric("Toplam kayıt", out _metricTotal, AccentBrush));
        return _overviewMetricsGrid;
    }

    private Border CreateOverviewMetric(
        string label,
        out TextBlock value,
        SolidColorBrush tone)
    {
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
        };
        header.Children.Add(new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = tone,
            Margin = new Thickness(0, 4, 9, 0),
        });
        header.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = SecondaryTextBrush,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
        });

        value = new TextBlock
        {
            Text = "—",
            FontSize = 29,
            FontWeight = FontWeights.SemiBold,
            Foreground = PrimaryTextBrush,
            Margin = new Thickness(0, 12, 0, 0),
        };

        return new Border
        {
            Margin = new Thickness(6, 0, 6, 12),
            Padding = new Thickness(18, 17, 18, 15),
            Background = SurfaceBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Child = new StackPanel
            {
                Children = { header, value },
            },
        };
    }

    private void RefreshOverviewMetrics(
        ControlCenterDashboardSnapshot snapshot)
    {
        _metricReady.Text = snapshot.ReadyCount.ToString();
        _metricInactive.Text = snapshot.Mcps.Count(state =>
            state.Health != ControlCenterHealthState.Ready &&
            ManagedMcpAggregateHealthPolicy.IsIgnored(
                state.Registration,
                state.Health,
                ManagedMcpSessionState.IsManuallyStopped(
                    state.Registration.Id))).ToString();
        _metricAttention.Text =
            (snapshot.AttentionCount + snapshot.OfflineCount).ToString();
        _metricTotal.Text = snapshot.Mcps.Count.ToString();
        _sidebarHealthText.Text = snapshot.HealthSummary;
        _sidebarHealthDot.Background = GetSummaryBrush(snapshot);
    }

    private void UpdateOverviewMetricsLayout()
    {
        if (_overviewMetricsGrid is not null)
        {
            _overviewMetricsGrid.Columns = ActualWidth >= 1300 ? 4 : 2;
        }
    }
}
