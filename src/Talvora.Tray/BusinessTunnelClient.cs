using System.Diagnostics;
using Talvora.Shared;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace Talvora.Tray;

internal static class BusinessTunnelClient
{
    private static readonly HttpClient Http = TalvoraHttp.CreateClient(
        timeout: TimeSpan.FromSeconds(5));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static string TunnelRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Talvora",
            "TunnelClient");

    private static string ConfigPath => Path.Combine(TunnelRoot, "business.json");

    private static string CredentialPath => Path.Combine(TunnelRoot, "runtime-key.dpapi");

    public static BusinessConfig LoadConfig()
    {
        if (!File.Exists(ConfigPath))
        {
            throw new FileNotFoundException(
                "ChatGPT Business tunnel yapılandırması bulunamadı.",
                ConfigPath);
        }

        var config = JsonSerializer.Deserialize<BusinessConfig>(
            File.ReadAllText(ConfigPath),
            JsonOptions);

        if (config is null ||
            string.IsNullOrWhiteSpace(config.Alias) ||
            string.IsNullOrWhiteSpace(config.TunnelId) ||
            string.IsNullOrWhiteSpace(config.McpUrl) ||
            string.IsNullOrWhiteSpace(config.TunnelClient) ||
            string.IsNullOrWhiteSpace(config.StateRoot))
        {
            throw new InvalidOperationException("ChatGPT Business tunnel yapılandırması geçersiz.");
        }

        return config;
    }

    public static async Task<TalvoraStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (!await IsLocalMcpHealthyAsync(cancellationToken))
        {
            return new TalvoraStatus(
                TalvoraConnectionState.Offline,
                "Talvora erişilemiyor",
                "Yerel MCP servisi çalışmıyor.");
        }

        BusinessConfig config;
        try
        {
            config = LoadConfig();
        }
        catch (Exception ex)
        {
            return new TalvoraStatus(
                TalvoraConnectionState.LocalOnly,
                "Talvora çalışıyor",
                ex.Message);
        }

        if (!File.Exists(config.TunnelClient))
        {
            return new TalvoraStatus(
                TalvoraConnectionState.LocalOnly,
                "Talvora çalışıyor, tunnel istemcisi yok",
                config.TunnelClient);
        }

        string? credential = null;
        try
        {
            credential = ReadRuntimeCredential();
            var status = await ReadTunnelStatusAsync(config, credential, cancellationToken);
            if (status is { ProcessRunning: true, Healthy: true, Ready: true })
            {
                return new TalvoraStatus(
                    TalvoraConnectionState.Ready,
                    "Talvora bağlı",
                    "ChatGPT Business tunnel hazır.");
            }

            return new TalvoraStatus(
                TalvoraConnectionState.LocalOnly,
                "Talvora çalışıyor, tunnel hazır değil",
                "İkona çift tıklayın veya menüden yeniden bağlanın.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            TrayLog.Write("Tunnel status check failed", ex);
            return new TalvoraStatus(
                TalvoraConnectionState.LocalOnly,
                "Talvora çalışıyor, tunnel bağlı değil",
                "İkona çift tıklayın veya menüden yeniden bağlanın.");
        }
        finally
        {
            credential = null;
        }
    }

    public static async Task<bool> IsLocalMcpHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Http.GetAsync(
                TalvoraConstants.HealthUrl,
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public static async Task ReconnectAsync(CancellationToken cancellationToken)
    {
        if (!await IsLocalMcpHealthyAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "Yerel Talvora MCP servisi çalışmıyor. Önce Talvora servisini başlatın.");
        }

        var config = LoadConfig();
        if (!File.Exists(config.TunnelClient))
        {
            throw new FileNotFoundException("OpenAI tunnel-client bulunamadı.", config.TunnelClient);
        }

        Directory.CreateDirectory(config.StateRoot);

        string? credential = null;
        try
        {
            credential = ReadRuntimeCredential();

            var connectArgs = new[]
            {
                "runtimes",
                "connect",
                "--alias",
                config.Alias,
                "--tunnel-id",
                config.TunnelId,
                "--runtime-api-key",
                "env:CONTROL_PLANE_API_KEY",
                "--mcp-server-url",
                config.McpUrl,
                "--json",
            };

            var connect = await RunClientAsync(
                config,
                connectArgs,
                credential,
                TimeSpan.FromSeconds(30),
                cancellationToken);

            if (connect.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"tunnel-client connect başarısız: {Collapse(connect.StandardError, connect.StandardOutput)}");
            }

            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var status = await ReadTunnelStatusAsync(
                        config,
                        credential,
                        cancellationToken);

                    if (status is { ProcessRunning: true, Healthy: true, Ready: true })
                    {
                        TrayLog.Write($"Reconnect succeeded. Alias={config.Alias}");
                        return;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    TrayLog.Write("Tunnel status not ready yet", ex);
                }

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }

            throw new TimeoutException("ChatGPT Business tunnel 90 saniye içinde hazır olmadı.");
        }
        finally
        {
            credential = null;
        }
    }

    public static string ReadRuntimeCredential()
    {
        if (!File.Exists(CredentialPath))
        {
            throw new FileNotFoundException(
                "Kaydedilmiş tunnel Runtime API key bulunamadı.",
                CredentialPath);
        }

        var hex = File.ReadAllText(CredentialPath).Trim();
        if (hex.Length == 0 || hex.Length % 2 != 0)
        {
            throw new InvalidOperationException("Kaydedilmiş Runtime API key biçimi geçersiz.");
        }

        byte[] encrypted;
        try
        {
            encrypted = Convert.FromHexString(hex);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "Kaydedilmiş Runtime API key DPAPI hex biçiminde değil.",
                ex);
        }

        var decrypted = NativeDpapi.Unprotect(encrypted);
        try
        {
            return Encoding.Unicode.GetString(decrypted).TrimEnd('\0');
        }
        finally
        {
            Array.Clear(decrypted);
            Array.Clear(encrypted);
        }
    }

    private static async Task<TunnelClientStatus?> ReadTunnelStatusAsync(
        BusinessConfig config,
        string credential,
        CancellationToken cancellationToken)
    {
        var result = await RunClientAsync(
            config,
            new[] { "runtimes", "status", config.Alias, "--json" },
            credential,
            TimeSpan.FromSeconds(15),
            cancellationToken);

        if (result.ExitCode != 0)
        {
            return null;
        }

        var json = ExtractJson(result.StandardOutput);
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        return new TunnelClientStatus
        {
            ProcessRunning = ReadBoolean(root, "process_running"),
            Healthy = ReadBoolean(root, "healthy"),
            Ready = ReadBoolean(root, "ready"),
        };
    }

    private static bool ReadBoolean(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.True;
    }

    private static async Task<ProcessExecutionResult> RunClientAsync(
        BusinessConfig config,
        IReadOnlyList<string> arguments,
        string credential,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var environment = new Dictionary<string, string?>
        {
            ["CONTROL_PLANE_API_KEY"] = credential,
            ["TUNNEL_CLIENT_STATE_DIR"] = config.StateRoot,
            ["LOG_LEVEL"] = "warn",
            ["ADMIN_UI_LOG_BUFFER_EVENTS"] = "500",
        };

        var result = await ProcessRunner.RunAsync(
            config.TunnelClient,
            config.StateRoot,
            arguments,
            environment,
            timeoutSeconds: Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds)),
            cancellationToken: cancellationToken);

        if (result.TimedOut)
        {
            throw new TimeoutException(
                $"tunnel-client komutu {timeout.TotalSeconds:F0} saniye içinde tamamlanmadı.");
        }

        return result;
    }

    private static string? ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end < start)
        {
            return null;
        }

        return text[start..(end + 1)];
    }

    private static string Collapse(params string[] values)
    {
        var value = string.Join(
            " ",
            values
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim()));

        return value.Length <= 400 ? value : value[..400];
    }

}
