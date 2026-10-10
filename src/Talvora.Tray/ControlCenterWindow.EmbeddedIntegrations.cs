using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Talvora.Shared;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Talvora.Tray;

internal sealed partial class ControlCenterWindow
{
    private sealed record EmbeddedMcpIntegrationEnvelope(
        int SchemaVersion,
        IReadOnlyList<EmbeddedMcpIntegrationCard> Integrations);

    private sealed record EmbeddedMcpIntegrationCard(
        string Id,
        string DisplayName,
        bool Ready,
        bool Authenticated,
        string Version,
        int ToolCount,
        DateTimeOffset CheckedAtUtc,
        DateTimeOffset? LastSuccessfulAtUtc,
        string? Detail);

    private static readonly HttpClient EmbeddedMcpHealthHttp =
        TalvoraHttp.CreateClient(timeout: TimeSpan.FromSeconds(22));
    private IReadOnlyList<EmbeddedMcpIntegrationCard> _embeddedMcpIntegrations = [];
    private bool _embeddedMcpHealthReachable;

    private async Task RefreshEmbeddedMcpIntegrationsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await EmbeddedMcpHealthHttp.GetFromJsonAsync<
                EmbeddedMcpIntegrationEnvelope>(
                    "http://127.0.0.1:7676/healthz/integrations",
                    cancellationToken);
            if (result is null || result.SchemaVersion != 1 ||
                result.Integrations is null ||
                result.Integrations.Count != 2)
            {
                _embeddedMcpHealthReachable = false;
                return;
            }

            _embeddedMcpIntegrations = result.Integrations;
            _embeddedMcpHealthReachable = true;
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is HttpRequestException or
            OperationCanceledException or
            InvalidOperationException or
            System.Text.Json.JsonException)
        {
            _embeddedMcpHealthReachable = false;
            // Embedded integrations are not required Windows services.
            // Their reachability must not turn the Talvora tray icon red.
        }
    }

    private void AppendEmbeddedMcpIntegrationSection(
        string searchText,
        int statusFilter)
    {
        var matches = _embeddedMcpIntegrations
            .Where(state =>
                (string.IsNullOrWhiteSpace(searchText) ||
                 state.DisplayName.Contains(
                     searchText, StringComparison.CurrentCultureIgnoreCase)) &&
                (statusFilter == 0 ||
                 (statusFilter == 1 && !state.Ready) ||
                 (statusFilter == 2 && state.Ready)))
            .ToArray();

        if (matches.Length == 0 &&
            (_embeddedMcpHealthReachable ||
             statusFilter != 0 ||
             !string.IsNullOrWhiteSpace(searchText)))
        {
            return;
        }

        var section = new StackPanel
        {
            Margin = new Thickness(0, 8, 0, 18),
        };
        section.Children.Add(new TextBlock
        {
            Text = "Gömülü MCP entegrasyonları",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = PrimaryTextBrush,
        });
        section.Children.Add(new TextBlock
        {
            Text = "Talvora çekirdeğinde çalışan, ayrı başlatma gerektirmeyen bağlantılar.",
            Margin = new Thickness(0, 4, 0, 12),
            FontSize = 11.5,
            Foreground = SecondaryTextBrush,
            TextWrapping = TextWrapping.Wrap,
        });
        if (!_embeddedMcpHealthReachable)
        {
            section.Children.Add(new TextBlock
            {
                Text = "Canlı entegrasyon sağlığı alınamıyor; önceki bilgiler güncel olmayabilir.",
                Margin = new Thickness(0, 0, 0, 9),
                Foreground = AttentionBrush,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        foreach (var item in matches)
        {
            var card = new StackPanel();
            var heading = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
            };
            heading.Children.Add(new TextBlock
            {
                Text = item.DisplayName,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = PrimaryTextBrush,
                Margin = new Thickness(0, 0, 10, 0),
            });
            heading.Children.Add(new TextBlock
            {
                Text = !_embeddedMcpHealthReachable
                    ? "Gözlemlenemiyor"
                    : item.Ready ? "Hazır" : "Dikkat",
                Foreground = !_embeddedMcpHealthReachable
                    ? TertiaryTextBrush
                    : item.Ready ? ReadyBrush : AttentionBrush,
                VerticalAlignment = VerticalAlignment.Center,
            });
            card.Children.Add(heading);

            var lastSuccessful = item.LastSuccessfulAtUtc is { } last
                ? last.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
                : "Henüz doğrulanmadı";
            card.Children.Add(new TextBlock
            {
                Text = $"Sürüm: {item.Version}  •  " +
                       $"Kimlik doğrulaması: {(item.Authenticated ? "Aktif" : "Doğrulanmadı")}  •  " +
                       $"Araçlar: {item.ToolCount}  •  Son başarı: {lastSuccessful}",
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = SecondaryTextBrush,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11.5,
            });
            if (!item.Ready &&
                _embeddedMcpHealthReachable &&
                !string.IsNullOrWhiteSpace(item.Detail))
            {
                card.Children.Add(new TextBlock
                {
                    Text = item.Detail,
                    Margin = new Thickness(0, 6, 0, 0),
                    Foreground = AttentionBrush,
                    TextWrapping = TextWrapping.Wrap,
                });
            }

            section.Children.Add(new Border
            {
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(16, 14, 16, 14),
                Background = SurfaceBrush,
                BorderBrush = CardBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Child = card,
            });
        }

        _cardsPanel.Children.Add(section);
    }
}
