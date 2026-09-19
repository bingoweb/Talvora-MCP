using System.Windows;
using System.Windows.Controls;
using Talvora.Shared;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using UiButton = Wpf.Ui.Controls.Button;

namespace Talvora.Tray;

internal sealed partial class ControlCenterWindow
{
    private UIElement BuildDetailView()
    {
        var content = new StackPanel
        {
            Margin = new Thickness(28, 24, 28, 28),
        };

        var backButton = new UiButton
        {
            Content = "Ana görünüm",
            Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowLeft20 },
            MinWidth = 124,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Style = FindStyle("TalvoraGhostButtonStyle"),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        backButton.Click += (_, _) => ShowDashboard();
        content.Children.Add(backButton);

        var heroGrid = new Grid
        {
            Margin = new Thickness(0, 18, 0, 0),
        };
        heroGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        heroGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
        });

        var titleStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
        };

        _detailTitle = new TextBlock
        {
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
        };
        titleStack.Children.Add(_detailTitle);

        _detailDescription = new TextBlock
        {
            Margin = new Thickness(0, 7, 24, 0),
            Foreground = SecondaryTextBrush,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
        };
        titleStack.Children.Add(_detailDescription);

        heroGrid.Children.Add(titleStack);

        _detailPrimaryActionButton = new UiButton
        {
            MinWidth = 148,
            MinHeight = 38,
            Margin = new Thickness(20, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Style = FindStyle("TalvoraPrimaryButtonStyle"),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        _detailPrimaryActionButton.Click += async (_, _) =>
            await ExecuteDetailPrimaryActionAsync();
        Grid.SetColumn(_detailPrimaryActionButton, 1);
        heroGrid.Children.Add(_detailPrimaryActionButton);

        content.Children.Add(heroGrid);

        _detailOperationText = new TextBlock
        {
            Foreground = SecondaryTextBrush,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };
        _detailOperationBanner = new Border
        {
            Margin = new Thickness(0, 16, 0, 0),
            Padding = new Thickness(13, 10, 13, 10),
            Background = RaisedSurfaceBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Visibility = Visibility.Collapsed,
            Child = _detailOperationText,
        };
        content.Children.Add(_detailOperationBanner);

        var controlsCard = new Border
        {
            Margin = new Thickness(0, 16, 0, 0),
            Style = FindStyle("TalvoraSubtleCardStyle"),
        };

        var controlsGrid = new Grid();
        controlsGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        controlsGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
        });

        var controlCopy = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
        };
        controlCopy.Children.Add(new TextBlock
        {
            Text = "Yaşam döngüsü",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
        });
        controlCopy.Children.Add(new TextBlock
        {
            Text = "MCP ve gerekli çalışma bileşenleri birlikte yönetilir.",
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = SecondaryTextBrush,
            FontSize = 12,
        });
        controlsGrid.Children.Add(controlCopy);

        var buttonRow = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _detailStartButton = new UiButton
        {
            Content = "Başlat",
            Icon = new SymbolIcon { Symbol = SymbolRegular.Play20 },
            MinWidth = 96,
            Style = FindStyle("TalvoraSecondaryButtonStyle"),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        _detailStartButton.Click += async (_, _) =>
            await ExecuteDetailLifecycleOperationAsync(
                ManagedMcpLifecycleOperation.Start);
        buttonRow.Children.Add(_detailStartButton);

        _detailStopButton = new UiButton
        {
            Content = "Durdur",
            Icon = new SymbolIcon { Symbol = SymbolRegular.Stop20 },
            Appearance = ControlAppearance.Danger,
            MinWidth = 96,
            Margin = new Thickness(8, 0, 0, 0),
            Style = FindStyle("TalvoraSecondaryButtonStyle"),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        _detailStopButton.Click += async (_, _) =>
            await ExecuteDetailLifecycleOperationAsync(
                ManagedMcpLifecycleOperation.Stop);
        buttonRow.Children.Add(_detailStopButton);

        _detailRestartButton = new UiButton
        {
            Content = "Yeniden başlat",
            Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowClockwise20 },
            MinWidth = 132,
            Margin = new Thickness(8, 0, 0, 0),
            Style = FindStyle("TalvoraSecondaryButtonStyle"),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        _detailRestartButton.Click += async (_, _) =>
            await ExecuteDetailLifecycleOperationAsync(
                ManagedMcpLifecycleOperation.Restart);
        buttonRow.Children.Add(_detailRestartButton);

        Grid.SetColumn(buttonRow, 1);
        controlsGrid.Children.Add(buttonRow);

        controlsCard.Child = controlsGrid;
        content.Children.Add(controlsCard);

        var summaryGrid = new Grid
        {
            Margin = new Thickness(0, 16, 0, 0),
        };
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(16),
        });
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });

        var statusCard = CreateDetailInfoCard("Durum", out _detailStatus);
        summaryGrid.Children.Add(statusCard);

        var versionCard = CreateDetailInfoCard("Sürüm", out _detailVersion);
        Grid.SetColumn(versionCard, 2);
        summaryGrid.Children.Add(versionCard);

        content.Children.Add(summaryGrid);

        var componentsCard = new Border
        {
            Margin = new Thickness(0, 16, 0, 0),
            Style = FindStyle("TalvoraCardStyle"),
        };

        var componentsRoot = new StackPanel();
        componentsRoot.Children.Add(new TextBlock
        {
            Text = "Bileşenler",
            Foreground = SecondaryTextBrush,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
        });
        componentsRoot.Children.Add(new TextBlock
        {
            Text = "MCP'nin çalışması için gereken zincir.",
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = TertiaryTextBrush,
            FontSize = 11,
        });

        _componentList = new StackPanel
        {
            Margin = new Thickness(0, 12, 0, 0),
        };
        componentsRoot.Children.Add(_componentList);
        componentsCard.Child = componentsRoot;
        content.Children.Add(componentsCard);

        var recentEventsCard = new Border
        {
            Margin = new Thickness(0, 16, 0, 0),
            Style = FindStyle("TalvoraCardStyle"),
        };
        var recentEventsRoot = new StackPanel();
        recentEventsRoot.Children.Add(new TextBlock
        {
            Text = "Son olaylar",
            Foreground = SecondaryTextBrush,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
        });
        recentEventsRoot.Children.Add(new TextBlock
        {
            Text = "Bu MCP için en son önemli yaşam döngüsü ve sağlık olayları.",
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = TertiaryTextBrush,
            FontSize = 11,
        });
        _detailRecentEventsList = new StackPanel
        {
            Margin = new Thickness(0, 12, 0, 0),
        };
        recentEventsRoot.Children.Add(_detailRecentEventsList);
        recentEventsCard.Child = recentEventsRoot;
        content.Children.Add(recentEventsCard);

        var technicalStack = new StackPanel();
        technicalStack.Children.Add(CreateTechnicalField(
            "Uç nokta (Endpoint)",
            out _detailEndpoint));
        technicalStack.Children.Add(CreateTechnicalField(
            "Taşıma (Transport)",
            out _detailTransport,
            new Thickness(0, 12, 0, 0)));
        technicalStack.Children.Add(CreateTechnicalField(
            "Tarayıcı / kanal",
            out _detailBrowserChannel,
            new Thickness(0, 12, 0, 0)));
        technicalStack.Children.Add(CreateTechnicalField(
            "Profil modu",
            out _detailProfileMode,
            new Thickness(0, 12, 0, 0)));
        technicalStack.Children.Add(CreateTechnicalField(
            "Profil yolu",
            out _detailProfilePath,
            new Thickness(0, 12, 0, 0)));
        technicalStack.Children.Add(CreateTechnicalField(
            "Runtime state yolu",
            out _detailRuntimeStatePath,
            new Thickness(0, 12, 0, 0)));
        technicalStack.Children.Add(CreateTechnicalField(
            "Browser smoke state yolu",
            out _detailBrowserSmokeStatePath,
            new Thickness(0, 12, 0, 0)));
        technicalStack.Children.Add(CreateTechnicalField(
            "Tünel (Tunnel)",
            out _detailTunnel,
            new Thickness(0, 12, 0, 0)));
        technicalStack.Children.Add(CreateTechnicalField(
            "Tünel kimliği",
            out _detailTunnelId,
            new Thickness(0, 12, 0, 0)));
        technicalStack.Children.Add(CreateTechnicalField(
            "Tünel yapılandırması",
            out _detailTunnelConfig,
            new Thickness(0, 12, 0, 0)));
        technicalStack.Children.Add(CreateTechnicalField(
            "Tünel çalışma dizini",
            out _detailTunnelStateRoot,
            new Thickness(0, 12, 0, 0)));
        technicalStack.Children.Add(CreateTechnicalField(
            "Yönetilen bileşenler",
            out _detailTechnicalComponents,
            new Thickness(0, 12, 0, 0)));

        _technicalDetailsExpander = new CardExpander
        {
            Margin = new Thickness(0, 16, 0, 0),
            Header = "Teknik ayrıntılar",
            Icon = new SymbolIcon { Symbol = SymbolRegular.Info20 },
            IsExpanded = false,
            Background = SurfaceBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            ContentPadding = new Thickness(20, 8, 20, 20),
            Content = technicalStack,
        };
        content.Children.Add(_technicalDetailsExpander);

        return content;
    }

    private Border CreateDetailInfoCard(
        string label,
        out TextBlock valueText,
        Thickness? margin = null)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = SecondaryTextBrush,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
        });

        valueText = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = PrimaryTextBrush,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
        };
        stack.Children.Add(valueText);

        return new Border
        {
            Margin = margin ?? new Thickness(0),
            Style = FindStyle("TalvoraCardStyle"),
            Child = stack,
        };
    }

    private Border CreateTechnicalField(
        string label,
        out TextBlock valueText,
        Thickness? margin = null)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = TertiaryTextBrush,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
        });

        valueText = new TextBlock
        {
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = PrimaryTextBrush,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };
        stack.Children.Add(valueText);

        return new Border
        {
            Margin = margin ?? new Thickness(0),
            Padding = new Thickness(12, 10, 12, 10),
            Background = RaisedSurfaceBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = stack,
        };
    }

    private async Task ShowDetailAsync(ManagedMcpDashboardState state)
    {
        _selectedMcp = state;

        _detailTitle.Text = state.Registration.DisplayName;
        _detailDescription.Text = state.Registration.Description;
        UpdateDetailSummary(state);
        UpdateDetailActionState(state);

        _detailEndpoint.Text = state.Registration.Endpoint;
        _detailTransport.Text = state.Registration.Transport;
        _detailBrowserChannel.Text = string.IsNullOrWhiteSpace(state.Registration.BrowserChannel)
            ? "Tanımlı değil"
            : state.Registration.BrowserChannel!;
        _detailProfileMode.Text = string.IsNullOrWhiteSpace(state.Registration.ProfileMode)
            ? "Tanımlı değil"
            : state.Registration.ProfileMode!;
        _detailProfilePath.Text = string.IsNullOrWhiteSpace(state.Registration.ProfilePath)
            ? "Tanımlı değil"
            : state.Registration.ProfilePath!;
        _detailRuntimeStatePath.Text = string.Join(
            Environment.NewLine,
            new[]
            {
                state.Registration.ProtocolProbe?.RuntimeGenerationStatePath,
                state.Registration.ProtocolProbe?.RuntimeProcessStatePath,
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (string.IsNullOrWhiteSpace(_detailRuntimeStatePath.Text))
        {
            _detailRuntimeStatePath.Text = "Tanımlı değil";
        }
        _detailBrowserSmokeStatePath.Text = string.IsNullOrWhiteSpace(
                state.Registration.ProtocolProbe?.BrowserSmokeStatePath)
            ? "Tanımlı değil"
            : state.Registration.ProtocolProbe!.BrowserSmokeStatePath!;
        _detailTunnel.Text = state.Registration.Tunnel is null
            ? "Bu MCP için tünel tanımlı değil."
            : state.Registration.Tunnel.Alias;
        _detailTunnelId.Text =
            string.IsNullOrWhiteSpace(state.Registration.Tunnel?.TunnelId)
                ? "Henüz tanımlı değil"
                : state.Registration.Tunnel!.TunnelId!;
        _detailTunnelConfig.Text =
            string.IsNullOrWhiteSpace(state.Registration.Tunnel?.ConfigPath)
                ? "Kayıt yok"
                : state.Registration.Tunnel!.ConfigPath!;
        _detailTunnelStateRoot.Text =
            string.IsNullOrWhiteSpace(state.Registration.Tunnel?.StateRoot)
                ? "Kayıt yok"
                : state.Registration.Tunnel!.StateRoot!;
        _detailTechnicalComponents.Text =
            state.Registration.Components.Count == 0
                ? "Ayrı bileşen kaydı yok"
                : string.Join(
                    Environment.NewLine,
                    state.Registration.Components.Select(component =>
                        string.IsNullOrWhiteSpace(component.Name)
                            ? $"{component.DisplayName} • {GetComponentKindLabel(component.Kind)}"
                            : $"{component.DisplayName} • {GetComponentKindLabel(component.Kind)} • {component.Name}"));

        RenderDetailRecentEvents(state.Registration.Id);

        _technicalDetailsExpander.IsExpanded = false;
        _detailOperationBanner.Visibility = Visibility.Collapsed;
        _detailVersion.Text = "Kontrol ediliyor...";

        _componentList.Children.Clear();
        _componentList.Children.Add(new TextBlock
        {
            Text = "Bileşen durumları kontrol ediliyor...",
            Foreground = SecondaryTextBrush,
            FontSize = 12,
        });

        _dashboardScroller.Visibility = Visibility.Collapsed;
        _detailScroller.Visibility = Visibility.Visible;
        _detailScroller.ScrollToTop();

        await RefreshDetailAsync();
    }

    private async Task RefreshDetailAsync()
    {
        var selected = _selectedMcp;
        if (selected is null || _lifetimeCts.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var componentTask =
                ControlCenterComponentHealthService.GetStatesAsync(
                    selected.Registration,
                    _lifetimeCts.Token);
            var versionTask =
                ControlCenterVersionService.GetVersionAsync(
                    selected.Registration,
                    _lifetimeCts.Token);

            await Task.WhenAll(componentTask, versionTask);

            if (_selectedMcp is null ||
                !string.Equals(
                    _selectedMcp.Registration.Id,
                    selected.Registration.Id,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _detailVersion.Text = versionTask.Result;
            RenderComponentStates(componentTask.Result);
            RenderDetailRecentEvents(selected.Registration.Id);
            UpdateDetailActionState(_selectedMcp);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            TrayLog.Write("Control Center detail refresh failed", ex);
            _componentList.Children.Clear();
            _componentList.Children.Add(new TextBlock
            {
                Text = "Bileşen durumları şu anda alınamıyor.",
                Foreground = OfflineBrush,
                FontSize = 12,
            });
        }
    }

    private void RenderDetailRecentEvents(string mcpId)
    {
        _detailRecentEventsList.Children.Clear();

        var events = ControlCenterEventStore
            .ReadRecent(TimeSpan.FromDays(7), maxRecords: 40)
            .Where(entry =>
                string.Equals(
                    entry.McpId,
                    mcpId,
                    StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .ToArray();

        if (events.Length == 0)
        {
            _detailRecentEventsList.Children.Add(new TextBlock
            {
                Text = "Bu MCP için son 7 günde önemli olay yok.",
                Foreground = SecondaryTextBrush,
                FontSize = 12,
            });
            return;
        }

        foreach (var entry in events)
        {
            var health = entry.Severity switch
            {
                ControlCenterEventSeverity.Error =>
                    ControlCenterHealthState.Offline,
                ControlCenterEventSeverity.Warning =>
                    ControlCenterHealthState.Attention,
                _ => ControlCenterHealthState.Ready,
            };

            var row = new Grid
            {
                Margin = new Thickness(0, 0, 0, 8),
            };
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto,
            });
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
            });

            row.Children.Add(new Border
            {
                Width = 7,
                Height = 7,
                Margin = new Thickness(0, 6, 10, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Background = GetHealthBrush(health),
                CornerRadius = new CornerRadius(4),
            });

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text =
                    $"{entry.LastOccurredAtUtc.ToLocalTime():dd.MM HH:mm} • {entry.Title}" +
                    (entry.Count > 1 ? $" ×{entry.Count}" : string.Empty),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            });
            if (!string.IsNullOrWhiteSpace(entry.Detail))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = entry.Detail,
                    Margin = new Thickness(0, 3, 0, 0),
                    Foreground = SecondaryTextBrush,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                });
            }

            Grid.SetColumn(stack, 1);
            row.Children.Add(stack);

            _detailRecentEventsList.Children.Add(row);
        }
    }

    private void UpdateDetailSummary(ManagedMcpDashboardState state)
    {
        _detailStatus.Text = $"{state.StatusText} — {state.Detail}";
        _detailStatus.Foreground = GetHealthBrush(state.Health);
    }

    private void RenderComponentStates(
        IReadOnlyList<ManagedMcpComponentState> componentStates)
    {
        _componentList.Children.Clear();

        foreach (var state in componentStates)
        {
            var row = new Grid
            {
                Margin = new Thickness(0, 0, 0, 10),
            };
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto,
            });
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
            });
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto,
            });

            var statusBrush = GetHealthBrush(state.Health);
            row.Children.Add(new Border
            {
                Width = 8,
                Height = 8,
                Margin = new Thickness(0, 5, 12, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Background = statusBrush,
                CornerRadius = new CornerRadius(4),
            });

            var textStack = new StackPanel();
            textStack.Children.Add(new TextBlock
            {
                Text = state.Component.DisplayName,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
            });
            textStack.Children.Add(new TextBlock
            {
                Text = $"{GetComponentKindLabel(state.Component.Kind)} • {state.Detail}",
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = SecondaryTextBrush,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
            });
            Grid.SetColumn(textStack, 1);
            row.Children.Add(textStack);

            var statusText = new TextBlock
            {
                Text = state.StatusText,
                Foreground = statusBrush,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
            };
            Grid.SetColumn(statusText, 2);
            row.Children.Add(statusText);

            _componentList.Children.Add(new Border
            {
                Padding = new Thickness(12),
                Background = RaisedSurfaceBrush,
                BorderBrush = CardBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = row,
            });
        }

        if (componentStates.Count == 0)
        {
            _componentList.Children.Add(new TextBlock
            {
                Text = "Bu MCP için ayrı bileşen kaydı yok.",
                Foreground = SecondaryTextBrush,
                FontSize = 12,
            });
        }
    }

    internal async Task RunSmokeScenarioAsync()
    {
        var dashboardDeadline = DateTime.UtcNow.AddSeconds(15);
        while ((_snapshot is null || _snapshot.Mcps.Count == 0) &&
               DateTime.UtcNow < dashboardDeadline)
        {
            await RefreshDashboardAsync();
            if (_snapshot is not null && _snapshot.Mcps.Count > 0)
            {
                break;
            }

            await Task.Delay(250);
        }
        if (_snapshot is null || _snapshot.Mcps.Count == 0)
        {
            throw new InvalidOperationException(
                "Control Center dashboard did not load any managed MCP.");
        }

        var first = _snapshot.Mcps[0];
        await ShowDetailAsync(first);

        if (_detailScroller.Visibility != Visibility.Visible ||
            _dashboardScroller.Visibility == Visibility.Visible)
        {
            throw new InvalidOperationException(
                "Control Center detail navigation did not activate.");
        }

        if (string.IsNullOrWhiteSpace(_detailVersion.Text) ||
            _componentList.Children.Count == 0 ||
            _detailPrimaryActionButton is null)
        {
            throw new InvalidOperationException(
                "Control Center detail view did not populate live metadata and actions.");
        }

        if (_technicalDetailsExpander.IsExpanded)
        {
            throw new InvalidOperationException(
                "Technical details should be collapsed by default.");
        }

        _technicalDetailsExpander.IsExpanded = true;
        if (string.IsNullOrWhiteSpace(_detailEndpoint.Text))
        {
            throw new InvalidOperationException(
                "Technical details did not contain the MCP endpoint.");
        }
        _technicalDetailsExpander.IsExpanded = false;

        ShowDashboard();

        if (_dashboardScroller.Visibility != Visibility.Visible ||
            _detailScroller.Visibility == Visibility.Visible)
        {
            throw new InvalidOperationException(
                "Control Center could not return to the dashboard.");
        }

        _eventsExpander.IsExpanded = true;
        RefreshEventsPanel();
        if (string.IsNullOrWhiteSpace(_eventsSummaryText.Text) ||
            _eventsList.Children.Count == 0)
        {
            throw new InvalidOperationException(
                "Control Center event summary panel did not render.");
        }

        _rawLogExpander.IsExpanded = true;
        RefreshRawLogView();
        if (!_rawLogTextBox.IsReadOnly ||
            string.IsNullOrWhiteSpace(_rawLogMetaText.Text))
        {
            throw new InvalidOperationException(
                "Control Center raw log viewer did not initialize.");
        }

        _rawLogExpander.IsExpanded = false;
        _eventsExpander.IsExpanded = false;
    }

    private void ShowDashboard()
    {
        _selectedMcp = null;
        _detailScroller.Visibility = Visibility.Collapsed;
        _dashboardScroller.Visibility = Visibility.Visible;
        _dashboardScroller.ScrollToTop();
    }

    private static string GetComponentKindLabel(string kind) =>
        kind switch
        {
            "windows-service" => "Windows servisi",
            "scheduled-task" => "Zamanlanmış görev",
            "process" => "İşlem",
            "mcp-protocol" => "MCP protokolü",
            "browser-runtime" => "Tarayıcı çalışma zamanı",
            "browser-smoke" => "Gerçek browser smoke",
            "tunnel" => "Tünel",
            _ => kind,
        };
}