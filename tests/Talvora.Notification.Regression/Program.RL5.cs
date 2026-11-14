using System.Net;
using System.Net.Http;
using System.Text;
using Talvora.Tray;

internal static partial class Program
{
    private sealed class Rl5FakeGitHubHandler : HttpMessageHandler
    {
        internal string Tag = "v0.0.2.0";
        internal string Url = "https://github.com/bingoweb/Talvora-MCP/releases/tag/v0.0.2.0";
        internal string Body = "";
        internal int PayloadBytes;
        internal int Calls;
        internal string? UserAgent;
        internal string? Accept;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (request.RequestUri?.AbsoluteUri !=
                "https://api.github.com/repos/bingoweb/Talvora-MCP/releases/latest")
                throw new InvalidOperationException("RL5: request may contact only the approved GitHub latest-release endpoint.");
            UserAgent = request.Headers.UserAgent.ToString();
            Accept = request.Headers.Accept.ToString();
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                tag_name = Tag,
                html_url = Url,
                body = Body,
            });
            PayloadBytes = Encoding.UTF8.GetByteCount(payload);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static async Task AssertRl5ReleaseCheckAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), "talvora-rl5-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var cache = Path.Combine(folder, "release-check.json");
            var mock = new Rl5FakeGitHubHandler();
            using var http = new HttpClient(mock);
            using var service = new ReleaseCheckService(http, "0.0.1.0+abcdef1", cache);
            await service.CheckOnceAsync(CancellationToken.None);
            if (mock.Calls != 1 || mock.UserAgent != "Talvora/0.0.1.0" ||
                mock.Accept != "application/vnd.github+json" ||
                http.Timeout != TimeSpan.FromSeconds(15) ||
                service.Current?.Version != "0.0.2.0" ||
                service.Current?.HtmlUrl != mock.Url ||
                service.Current?.CheckedAtUtc is null ||
                !File.Exists(cache))
                throw new InvalidOperationException("RL5: newer validated GitHub version is not stored with its approved link and request headers.");
            using var reloadHttp = new HttpClient(mock, disposeHandler: false);
            using (var reloaded = new ReleaseCheckService(reloadHttp, "0.0.1.0+abcdef1", cache))
            {
                if (reloaded.Current?.Version != "0.0.2.0")
                    throw new InvalidOperationException("RL5: the persisted newer release is not loaded when the tray restarts.");
            }

            foreach (var invalid in new[]
            {
                (Tag: "v0.0.1.0", Url: mock.Url),
                (Tag: "v0.0.0.9", Url: mock.Url),
                (Tag: "v0.0.2.0-rc1", Url: mock.Url),
                (Tag: "v1.0.2.0", Url: mock.Url),
                (Tag: "v0.0.2.0", Url: "https://attacker.invalid/releases/tag/v0.0.2.0"),
                (Tag: "v0.0.2.0", Url: "http://github.com/bingoweb/Talvora-MCP/releases/tag/v0.0.2.0"),
                (Tag: "v0.0.2.0", Url: "https://github.com/bingoweb/Talvora-MCP/releases.evil/tag/v0.0.2.0"),
            })
            {
                mock.Tag = invalid.Tag;
                mock.Url = invalid.Url;
                await service.CheckOnceAsync(CancellationToken.None);
                if (service.Current is not null)
                    throw new InvalidOperationException("RL5: non-newer, invalid or unsafe GitHub release must not show a notification: " + invalid);
            }

            mock.Body = new string('n', 40_000);
            mock.Tag = "v0.0.3.0";
            mock.Url = "https://github.com/bingoweb/Talvora-MCP/releases/tag/v0.0.3.0";
            await service.CheckOnceAsync(CancellationToken.None);
            if (mock.PayloadBytes is < 39_000 or > 45_000 ||
                service.Current?.Version != "0.0.3.0" ||
                service.Current?.HtmlUrl != mock.Url || !File.Exists(cache))
                throw new InvalidOperationException(
                    "D74: a 40 KB GitHub release response with long notes must set the newer release state.");

            var accepted = service.Current;
            mock.Body = new string('n', 262_145);
            mock.Tag = "v0.0.4.0";
            mock.Url = "https://github.com/bingoweb/Talvora-MCP/releases/tag/v0.0.4.0";
            await service.CheckOnceAsync(CancellationToken.None);
            if (mock.PayloadBytes <= 262_144 || service.Current != accepted)
                throw new InvalidOperationException(
                    "D74: a GitHub release response over 256 KB must be ignored without changing current state.");

            using var devHttp = new HttpClient(mock, disposeHandler: false);
            using var development = new ReleaseCheckService(devHttp, "0.0.0.0-dev+abc1234", cache);
            var before = mock.Calls;
            await development.CheckOnceAsync(CancellationToken.None);
            if (mock.Calls != before || development.Current is not null)
                throw new InvalidOperationException("RL5: development versions must not make even one GitHub request or show cached news.");
            var shared = ReleaseCheckService.Instance;
            shared.ForceNewVersionForSmoke("");
            var shell = new Talvora.Tray.ViewModels.ShellViewModel(() => Task.CompletedTask);
            var settings = new Talvora.Tray.ViewModels.SettingsViewModel();
            if (shell.IsNewVersionKnown || settings.HasNewVersion ||
                shell.NewVersionHelpText.Length > 0)
                throw new InvalidOperationException("RL5: no release should leave the rail and About section quiet.");
            shared.ForceNewVersionForSmoke("0.0.9.9");
            if (!shell.IsNewVersionKnown || !settings.HasNewVersion ||
                shell.NewVersionHelpText != "Yeni sürüm hazır" ||
                settings.NewVersionText != "Yeni sürüm hazır: 0.0.9.9" ||
                settings.NewVersionHtmlUrl?.AbsoluteUri !=
                    "https://github.com/bingoweb/Talvora-MCP/releases/tag/v0.0.9.9" ||
                Talvora.Tray.Controls.NavRailItem.HasDotProperty.DefaultMetadata.DefaultValue is not false)
                throw new InvalidOperationException("RL5: rail dot, screen text or secure release-notes link were not bound.");
            shared.ForceNewVersionForSmoke("");
            if (shell.IsNewVersionKnown || settings.HasNewVersion)
                throw new InvalidOperationException("RL5: the new-version state did not clear.");
            Console.WriteLine("RL5_RELEASE_CHECK_GREEN");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
