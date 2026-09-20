using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using UiButton = Wpf.Ui.Controls.Button;
using UiTextBox = Wpf.Ui.Controls.TextBox;
using UiPasswordBox = Wpf.Ui.Controls.PasswordBox;
using TextBlock = System.Windows.Controls.TextBlock;
using HAlign = System.Windows.HorizontalAlignment;
using WpfCursors = System.Windows.Input.Cursors;

namespace Talvora.Tray;

internal sealed partial class ControlCenterWindow
{
    private Border BuildSetupCard()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
        });
        root.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });

        root.Children.Add(new Border
        {
            Width = 38,
            Height = 38,
            Margin = new Thickness(0, 1, 14, 0),
            Background = AttentionSoftBrush,
            BorderBrush = AttentionBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new SymbolIcon
            {
                Symbol = SymbolRegular.Wrench20,
                Foreground = AttentionBrush,
                Width = 20,
                Height = 20,
            },
        });

        var content = new StackPanel();

        _setupTitleText = new TextBlock
        {
            Text = "Kurulumu Tamamla",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
        };
        content.Children.Add(_setupTitleText);

        _setupDetailText = new TextBlock
        {
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = SecondaryTextBrush,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };
        content.Children.Add(_setupDetailText);

        var existingTunnelHeading = new TextBlock
        {
            Text = "Mevcut OpenAI tunnel kayıtları",
            Margin = new Thickness(0, 14, 0, 5),
            Foreground = SecondaryTextBrush,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Visibility = Visibility.Collapsed,
        };
        existingTunnelHeading.SetValue(
            FrameworkElement.TagProperty,
            "existing-tunnel-heading");
        content.Children.Add(existingTunnelHeading);

        var devTunnelLabel = new TextBlock
        {
            Text = "Talvora Dev tunnel ID",
            Margin = new Thickness(0, 6, 0, 5),
            Foreground = SecondaryTextBrush,
            FontSize = 11,
            Visibility = Visibility.Collapsed,
        };
        devTunnelLabel.SetValue(
            FrameworkElement.TagProperty,
            "dev-tunnel-label");
        content.Children.Add(devTunnelLabel);

        _setupDevTunnelIdBox = new UiTextBox
        {
            MinHeight = 40,
            MaxWidth = 520,
            MaxLength = 39,
            HorizontalAlignment = HAlign.Left,
            Padding = new Thickness(10, 7, 10, 7),
            FontFamily = FontFamily,
            FontSize = 13,
            Background = RaisedSurfaceBrush,
            Foreground = PrimaryTextBrush,
            BorderBrush = StrongBorderBrush,
            BorderThickness = new Thickness(1),
            PlaceholderText = "tunnel_...",
            Visibility = Visibility.Collapsed,
        };
        _setupDevTunnelIdBox.TextChanged += (_, _) =>
            RefreshSetupActionAvailability();
        content.Children.Add(_setupDevTunnelIdBox);

        var adminTunnelLabel = new TextBlock
        {
            Text = "Talvora Admin tunnel ID",
            Margin = new Thickness(0, 8, 0, 5),
            Foreground = SecondaryTextBrush,
            FontSize = 11,
            Visibility = Visibility.Collapsed,
        };
        adminTunnelLabel.SetValue(
            FrameworkElement.TagProperty,
            "admin-tunnel-label");
        content.Children.Add(adminTunnelLabel);

        _setupAdminTunnelIdBox = new UiTextBox
        {
            MinHeight = 40,
            MaxWidth = 520,
            MaxLength = 39,
            HorizontalAlignment = HAlign.Left,
            Padding = new Thickness(10, 7, 10, 7),
            FontFamily = FontFamily,
            FontSize = 13,
            Background = RaisedSurfaceBrush,
            Foreground = PrimaryTextBrush,
            BorderBrush = StrongBorderBrush,
            BorderThickness = new Thickness(1),
            PlaceholderText = "tunnel_...",
            Visibility = Visibility.Collapsed,
        };
        _setupAdminTunnelIdBox.TextChanged += (_, _) =>
            RefreshSetupActionAvailability();
        content.Children.Add(_setupAdminTunnelIdBox);

        var existingTunnelHelp = new TextBlock
        {
            Text =
                "Tunnel OpenAI tarafında zaten oluşturulduysa ID'yi buraya yapıştırın. " +
                "Talvora mevcut DPAPI-korumalı Runtime API key'i yeniden kullanır; Admin API key gerekmez.",
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = TertiaryTextBrush,
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        existingTunnelHelp.SetValue(
            FrameworkElement.TagProperty,
            "existing-tunnel-help");
        content.Children.Add(existingTunnelHelp);

        var adminKeyLabel = new TextBlock
        {
            Text = "OpenAI Admin API key",
            Margin = new Thickness(0, 14, 0, 5),
            Foreground = SecondaryTextBrush,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
        };
        adminKeyLabel.SetValue(FrameworkElement.TagProperty, "admin-key-label");
        content.Children.Add(adminKeyLabel);

        _setupAdminKeyBox = new UiPasswordBox
        {
            MinHeight = 40,
            MaxWidth = 520,
            HorizontalAlignment = HAlign.Left,
            Padding = new Thickness(10, 7, 10, 7),
            FontFamily = FontFamily,
            FontSize = 13,
            Background = RaisedSurfaceBrush,
            Foreground = PrimaryTextBrush,
            BorderBrush = StrongBorderBrush,
            BorderThickness = new Thickness(1),
            PasswordChar = '●',
            PlaceholderText = "Admin API key",
        };
        _setupAdminKeyBox.PasswordChanged += (_, _) =>
        {
            RefreshSetupActionAvailability();
        };
        content.Children.Add(_setupAdminKeyBox);

        content.Children.Add(new TextBlock
        {
            Text =
                "Bu anahtar yalnız tunnel CRUD işlemi sırasında kullanılır; Runtime key yerine geçmez. " +
                "Kaydedildikten sonra değer tekrar ekranda gösterilmez.",
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = TertiaryTextBrush,
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
        });

        _setupActionButton = new UiButton
        {
            Content = "Kaydet ve kurulumu tamamla",
            Icon = new SymbolIcon { Symbol = SymbolRegular.Checkmark20 },
            MinWidth = 190,
            Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HAlign.Left,
            Style = FindStyle("TalvoraPrimaryButtonStyle"),
            Cursor = WpfCursors.Hand,
        };
        _setupActionButton.Click += async (_, _) =>
            await CompleteSetupFromDashboardAsync();
        content.Children.Add(_setupActionButton);

        Grid.SetColumn(content, 1);
        root.Children.Add(content);

        return new Border
        {
            Margin = new Thickness(0, 0, 0, 22),
            Padding = new Thickness(16),
            Background = AttentionSoftBrush,
            BorderBrush = AttentionBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Visibility = Visibility.Collapsed,
            Child = root,
        };
    }

    private void RefreshSetupCard()
    {
        if (_setupCard is null || _snapshot is null)
        {
            return;
        }

        var registry = new Talvora.Shared.ManagedMcpRegistryDocument
        {
            Mcps = _snapshot.Mcps
                .Select(state => state.Registration)
                .ToList(),
        };

        var setup = ControlCenterSetupService.Evaluate(registry);
        _setupState = setup;
        if (setup.IsComplete)
        {
            _setupDevTunnelIdBox.Text = string.Empty;
            _setupAdminTunnelIdBox.Text = string.Empty;
            _setupAdminKeyBox.Password = string.Empty;
            _setupCard.Visibility = Visibility.Collapsed;
            return;
        }

        _setupCard.Visibility = Visibility.Visible;
        _setupTitleText.Text = "Kurulumu Tamamla";
        _setupDetailText.Text =
            $"{setup.Summary}  {setup.Detail}";

        var adminLabel = FindTaggedElement<TextBlock>(
            _setupCard,
            "admin-key-label");
        var existingTunnelHeading = FindTaggedElement<TextBlock>(
            _setupCard,
            "existing-tunnel-heading");
        var existingTunnelHelp = FindTaggedElement<TextBlock>(
            _setupCard,
            "existing-tunnel-help");
        var devTunnelLabel = FindTaggedElement<TextBlock>(
            _setupCard,
            "dev-tunnel-label");
        var adminTunnelLabel = FindTaggedElement<TextBlock>(
            _setupCard,
            "admin-tunnel-label");

        var needsDevTunnelId =
            setup.MissingTunnelRegistrationIds.Contains(
                "talvora-dev",
                StringComparer.OrdinalIgnoreCase);
        var needsAdminTunnelId =
            setup.MissingTunnelRegistrationIds.Contains(
                "talvora-admin",
                StringComparer.OrdinalIgnoreCase);
        var showExistingTunnelFields =
            needsDevTunnelId || needsAdminTunnelId;

        if (existingTunnelHeading is not null)
        {
            existingTunnelHeading.Visibility =
                showExistingTunnelFields
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        if (existingTunnelHelp is not null)
        {
            existingTunnelHelp.Visibility =
                showExistingTunnelFields
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        if (devTunnelLabel is not null)
        {
            devTunnelLabel.Visibility =
                needsDevTunnelId
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
        _setupDevTunnelIdBox.Visibility =
            needsDevTunnelId
                ? Visibility.Visible
                : Visibility.Collapsed;
        if (!needsDevTunnelId)
        {
            _setupDevTunnelIdBox.Text = string.Empty;
        }

        if (adminTunnelLabel is not null)
        {
            adminTunnelLabel.Visibility =
                needsAdminTunnelId
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
        _setupAdminTunnelIdBox.Visibility =
            needsAdminTunnelId
                ? Visibility.Visible
                : Visibility.Collapsed;
        if (!needsAdminTunnelId)
        {
            _setupAdminTunnelIdBox.Text = string.Empty;
        }

        if (setup.NeedsAdminCredential)
        {
            if (adminLabel is not null)
            {
                adminLabel.Visibility = Visibility.Visible;
            }

            _setupAdminKeyBox.Visibility = Visibility.Visible;
            _setupActionButton.Content = showExistingTunnelFields
                ? "Tünelleri bağla / kurulumu tamamla"
                : "Kaydet ve kurulumu tamamla";
            _setupActionButton.Icon =
                new SymbolIcon { Symbol = SymbolRegular.Checkmark20 };
        }
        else
        {
            if (adminLabel is not null)
            {
                adminLabel.Visibility = Visibility.Collapsed;
            }

            _setupAdminKeyBox.Password = string.Empty;
            _setupAdminKeyBox.Visibility = Visibility.Collapsed;
            _setupActionButton.Content = setup.RuntimeFoundationMissing
                ? "Yeniden denetle"
                : "Kurulumu tamamla";
            _setupActionButton.Icon = new SymbolIcon
            {
                Symbol = SymbolRegular.ArrowClockwise20,
            };
        }

        RefreshSetupActionAvailability();
    }

    private void RefreshSetupActionAvailability()
    {
        if (_setupActionButton is null ||
            _setupState is null)
        {
            return;
        }

        if (_setupState.IsComplete)
        {
            _setupActionButton.IsEnabled = false;
            return;
        }

        if (_setupState.RuntimeFoundationMissing ||
            !_setupState.NeedsAdminCredential)
        {
            _setupActionButton.IsEnabled = true;
            return;
        }

        if (!string.IsNullOrWhiteSpace(
                _setupAdminKeyBox.Password))
        {
            _setupActionButton.IsEnabled = true;
            return;
        }

        var allExistingTunnelIdsProvided =
            _setupState.MissingTunnelRegistrationIds.All(id =>
                id switch
                {
                    "talvora-dev" =>
                        _setupDevTunnelIdBox.Visibility == Visibility.Visible &&
                        ManagedMcpTunnelProvisioningService.IsValidTunnelId(
                            _setupDevTunnelIdBox.Text?.Trim()),
                    "talvora-admin" =>
                        _setupAdminTunnelIdBox.Visibility == Visibility.Visible &&
                        ManagedMcpTunnelProvisioningService.IsValidTunnelId(
                            _setupAdminTunnelIdBox.Text?.Trim()),
                    _ => false,
                });

        _setupActionButton.IsEnabled =
            allExistingTunnelIdsProvided;
    }

    private async Task CompleteSetupFromDashboardAsync()
    {
        if (_setupActionButton is null ||
            _lifetimeCts.IsCancellationRequested)
        {
            return;
        }

        _setupActionButton.IsEnabled = false;
        var adminKey = _setupAdminKeyBox.Visibility == Visibility.Visible
            ? _setupAdminKeyBox.Password
            : null;
        var existingTunnelIds =
            new Dictionary<string, string?>(
                StringComparer.OrdinalIgnoreCase);

        if (_setupDevTunnelIdBox.Visibility == Visibility.Visible &&
            !string.IsNullOrWhiteSpace(_setupDevTunnelIdBox.Text))
        {
            existingTunnelIds["talvora-dev"] =
                _setupDevTunnelIdBox.Text.Trim();
        }

        if (_setupAdminTunnelIdBox.Visibility == Visibility.Visible &&
            !string.IsNullOrWhiteSpace(_setupAdminTunnelIdBox.Text))
        {
            existingTunnelIds["talvora-admin"] =
                _setupAdminTunnelIdBox.Text.Trim();
        }

        try
        {
            var state = await ControlCenterSetupService.CompleteAsync(
                adminKey,
                existingTunnelIds,
                _lifetimeCts.Token);

            _setupDevTunnelIdBox.Text = string.Empty;
            _setupAdminTunnelIdBox.Text = string.Empty;
            _setupAdminKeyBox.Password = string.Empty;

            if (state.IsComplete)
            {
                _setupCard.Visibility = Visibility.Collapsed;
                await RefreshDashboardAsync();
                return;
            }

            _setupDetailText.Text = state.Detail;
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            TrayLog.Write("Control Center setup completion failed", ex);
            ControlCenterEventStore.Record(
                ControlCenterEventSeverity.Warning,
                "setup",
                "Kurulum tamamlanamadı",
                GetFriendlySetupError(ex),
                dedupKey: "setup:completion-failed");

            _setupDetailText.Text = GetFriendlySetupError(ex);
            await ShowOperationErrorAsync(
                "Kurulum tamamlanamadı",
                GetFriendlySetupError(ex));
        }
        finally
        {
            adminKey = null;
            existingTunnelIds.Clear();
            _setupDevTunnelIdBox.Text = string.Empty;
            _setupAdminTunnelIdBox.Text = string.Empty;
            _setupAdminKeyBox.Password = string.Empty;
            RefreshSetupCard();
        }
    }

    private static string GetFriendlySetupError(Exception exception)
    {
        if (exception.Message.Contains(
                "tunnel ID",
                StringComparison.CurrentCultureIgnoreCase))
        {
            return "Mevcut tunnel ID geçersiz veya bu MCP kaydıyla bağlanamadı. OpenAI tunnel kaydını ve ID biçimini kontrol edin.";
        }

        if (exception.Message.Contains(
                "Admin API key",
                StringComparison.CurrentCultureIgnoreCase))
        {
            return "Yeni tünel oluşturmak için geçerli OpenAI Admin API key gerekiyor.";
        }

        if (exception.Message.Contains(
                "Runtime API key",
                StringComparison.CurrentCultureIgnoreCase) ||
            exception.Message.Contains(
                "temel kurulum",
                StringComparison.CurrentCultureIgnoreCase))
        {
            return "Güvenli MCP tünelinin Runtime temel kurulumu eksik. Mevcut Business tünel kurulumunu tamamlayıp yeniden deneyin.";
        }

        return "Kurulum tamamlanamadı. Son olay ve ham günlük ayrıntılarını kontrol edin.";
    }

    private static T? FindTaggedElement<T>(
        DependencyObject root,
        object tag)
        where T : FrameworkElement
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child =
                System.Windows.Media.VisualTreeHelper.GetChild(root, index);

            if (child is T typed &&
                Equals(typed.Tag, tag))
            {
                return typed;
            }

            var nested = FindTaggedElement<T>(child, tag);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}