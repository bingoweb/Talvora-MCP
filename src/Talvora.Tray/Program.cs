using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace Talvora.Tray;

internal enum TalvoraConnectionState
{
    Ready,
    LocalOnly,
    Offline,
}

internal sealed record TalvoraStatus(
    TalvoraConnectionState State,
    string Summary,
    string Detail);

internal sealed record BusinessConfig(
    string Alias,
    string TunnelId,
    string McpUrl,
    string TunnelClient,
    string TunnelClientVersion,
    string StateRoot,
    string UpdatedAtUtc);

internal sealed class TunnelClientStatus
{
    public bool ProcessRunning { get; init; }
    public bool Healthy { get; init; }
    public bool Ready { get; init; }
}

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--reconnect", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                BusinessTunnelClient.ReconnectAsync(CancellationToken.None).GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception ex)
            {
                TrayLog.Write("Reconnect command failed", ex);
                return 1;
            }
        }

        if (args.Any(arg => string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var config = BusinessTunnelClient.LoadConfig();
                _ = BusinessTunnelClient.ReadRuntimeCredential();
                TrayLog.Write($"Self-test succeeded. Alias={config.Alias}");
                return 0;
            }
            catch (Exception ex)
            {
                TrayLog.Write("Self-test failed", ex);
                return 1;
            }
        }

        using var mutex = new Mutex(initiallyOwned: true, @"Local\Talvora.Tray", out var createdNew);
        if (!createdNew)
        {
            return 0;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayApplicationContext());
        return 0;
    }
}

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _reconnectItem;
    private readonly ToolStripMenuItem _refreshItem;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Windows.Forms.Timer _shutdownTimer;
    private readonly EventWaitHandle _shutdownEvent = new(
        initialState: false,
        EventResetMode.AutoReset,
        @"Local\Talvora.Tray.Shutdown");
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private Icon? _statusIcon;
    private int _automaticReconnectFailures;
    private DateTime _nextAutomaticReconnectUtc = DateTime.MinValue;
    private TalvoraStatus _lastStatus = new(
        TalvoraConnectionState.LocalOnly,
        "Talvora durumu kontrol ediliyor...",
        string.Empty);

    public TrayApplicationContext()
    {
        _statusItem = new ToolStripMenuItem("Talvora durumu kontrol ediliyor...")
        {
            Enabled = false,
        };

        _reconnectItem = new ToolStripMenuItem("ChatGPT Business'a yeniden bağlan");
        _reconnectItem.Click += async (_, _) => await ReconnectAsync();

        _refreshItem = new ToolStripMenuItem("Durumu yenile");
        _refreshItem.Click += async (_, _) => await RefreshStatusAsync(showBalloon: true);

        var exitItem = new ToolStripMenuItem("Tepsi uygulamasından çık");
        exitItem.Click += (_, _) => ExitTray();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_reconnectItem);
        menu.Items.Add(_refreshItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Text = "Talvora",
            Visible = true,
        };
        _notifyIcon.DoubleClick += async (_, _) => await ReconnectAsync();
        _notifyIcon.MouseClick += async (_, e) =>
        {
            if (e.Button == MouseButtons.Left && _lastStatus.State != TalvoraConnectionState.Ready)
            {
                await ReconnectAsync();
            }
        };

        SetTrayState(
            TalvoraConnectionState.LocalOnly,
            "Talvora durumu kontrol ediliyor...",
            "Çift tıklayın: yeniden bağlan");

        _timer = new System.Windows.Forms.Timer
        {
            Interval = 5_000,
            Enabled = true,
        };
        _timer.Tick += async (_, _) => await MaintainConnectionAsync();

        _shutdownTimer = new System.Windows.Forms.Timer
        {
            Interval = 250,
            Enabled = true,
        };
        _shutdownTimer.Tick += (_, _) =>
        {
            if (_shutdownEvent.WaitOne(0))
            {
                ExitTray();
            }
        };

        _ = MaintainConnectionAsync();
    }

    private async Task MaintainConnectionAsync()
    {
        if (!await _operationGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            var status = await BusinessTunnelClient.GetStatusAsync(CancellationToken.None);
            if (status.State == TalvoraConnectionState.Ready)
            {
                ResetAutomaticReconnectBackoff();
                SetTrayState(status.State, status.Summary, status.Detail);
                return;
            }

            if (status.State == TalvoraConnectionState.Offline)
            {
                _timer.Interval = 5_000;
                _nextAutomaticReconnectUtc = DateTime.UtcNow.AddSeconds(5);
                SetTrayState(
                    TalvoraConnectionState.LocalOnly,
                    "Talvora başlatılıyor...",
                    "Yerel servis bekleniyor; ChatGPT Business otomatik bağlanacak.");
                return;
            }

            SetTrayState(status.State, status.Summary, status.Detail);

            if (DateTime.UtcNow < _nextAutomaticReconnectUtc)
            {
                return;
            }

            _reconnectItem.Enabled = false;
            _refreshItem.Enabled = false;
            SetTrayState(
                TalvoraConnectionState.LocalOnly,
                "ChatGPT Business otomatik bağlanıyor...",
                "Tunnel hazır olana kadar Talvora otomatik yeniden deneyecek.");

            try
            {
                await BusinessTunnelClient.ReconnectAsync(CancellationToken.None);
                status = await BusinessTunnelClient.GetStatusAsync(CancellationToken.None);
                SetTrayState(status.State, status.Summary, status.Detail);

                if (status.State == TalvoraConnectionState.Ready)
                {
                    ResetAutomaticReconnectBackoff();
                    var alias = BusinessTunnelClient.LoadConfig().Alias;
                    TrayLog.Write($"Automatic reconnect succeeded. Alias={alias}");
                    return;
                }

                throw new InvalidOperationException(
                    "Tunnel connect tamamlandı ancak bağlantı henüz hazır değil.");
            }
            catch (Exception ex)
            {
                _automaticReconnectFailures++;
                var delay = GetAutomaticReconnectDelay(_automaticReconnectFailures);
                _nextAutomaticReconnectUtc = DateTime.UtcNow.Add(delay);
                _timer.Interval = 5_000;

                TrayLog.Write(
                    $"Automatic reconnect failed. Attempt={_automaticReconnectFailures}; RetryIn={delay.TotalSeconds:F0}s",
                    ex);

                var localHealthy = await BusinessTunnelClient.IsLocalMcpHealthyAsync(
                    CancellationToken.None);

                SetTrayState(
                    localHealthy ? TalvoraConnectionState.LocalOnly : TalvoraConnectionState.Offline,
                    localHealthy
                        ? "Talvora çalışıyor, Business bağlantısı bekleniyor"
                        : "Talvora servisi bekleniyor",
                    $"Otomatik yeniden deneme {delay.TotalSeconds:F0} saniye sonra.");
            }
            finally
            {
                _reconnectItem.Enabled = true;
                _refreshItem.Enabled = true;
            }
        }
        catch (Exception ex)
        {
            _automaticReconnectFailures++;
            var delay = GetAutomaticReconnectDelay(_automaticReconnectFailures);
            _nextAutomaticReconnectUtc = DateTime.UtcNow.Add(delay);
            _timer.Interval = 5_000;
            TrayLog.Write(
                $"Automatic connection maintenance failed. Attempt={_automaticReconnectFailures}; RetryIn={delay.TotalSeconds:F0}s",
                ex);
            SetTrayState(
                TalvoraConnectionState.LocalOnly,
                "Talvora bağlantısı hazırlanıyor",
                $"Otomatik yeniden deneme {delay.TotalSeconds:F0} saniye sonra.");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private void ResetAutomaticReconnectBackoff()
    {
        _automaticReconnectFailures = 0;
        _nextAutomaticReconnectUtc = DateTime.MinValue;
        _timer.Interval = 30_000;
    }

    private static TimeSpan GetAutomaticReconnectDelay(int failureCount)
    {
        var seconds = failureCount switch
        {
            <= 1 => 5,
            2 => 10,
            3 => 20,
            4 => 30,
            _ => 60,
        };

        return TimeSpan.FromSeconds(seconds);
    }

    private async Task RefreshStatusAsync(bool showBalloon)
    {
        if (!await _operationGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            var status = await BusinessTunnelClient.GetStatusAsync(CancellationToken.None);
            SetTrayState(status.State, status.Summary, status.Detail);
            if (showBalloon)
            {
                ShowBalloon(status.Summary, status.Detail);
            }
        }
        catch (Exception ex)
        {
            TrayLog.Write("Status refresh failed", ex);
            SetTrayState(
                TalvoraConnectionState.Offline,
                "Talvora durumu alınamadı",
                ex.Message);
            if (showBalloon)
            {
                ShowBalloon("Talvora", ex.Message, ToolTipIcon.Error);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task ReconnectAsync()
    {
        if (!await _operationGate.WaitAsync(0))
        {
            return;
        }

        _reconnectItem.Enabled = false;
        _refreshItem.Enabled = false;

        try
        {
            SetTrayState(
                TalvoraConnectionState.LocalOnly,
                "ChatGPT Business yeniden bağlanıyor...",
                "Tunnel hazırlanıyor.");

            await BusinessTunnelClient.ReconnectAsync(CancellationToken.None);

            var status = await BusinessTunnelClient.GetStatusAsync(CancellationToken.None);
            SetTrayState(status.State, status.Summary, status.Detail);
            ShowBalloon(
                "Talvora bağlantısı hazır",
                "ChatGPT Business tunnel yeniden bağlandı.",
                ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            TrayLog.Write("Reconnect failed", ex);
            var localHealthy = await BusinessTunnelClient.IsLocalMcpHealthyAsync(CancellationToken.None);
            SetTrayState(
                localHealthy ? TalvoraConnectionState.LocalOnly : TalvoraConnectionState.Offline,
                localHealthy ? "Talvora çalışıyor, tunnel bağlı değil" : "Talvora erişilemiyor",
                ex.Message);
            ShowBalloon("Talvora yeniden bağlanamadı", ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            _reconnectItem.Enabled = true;
            _refreshItem.Enabled = true;
            _operationGate.Release();
        }
    }

    private void SetTrayState(TalvoraConnectionState state, string summary, string detail)
    {
        _lastStatus = new TalvoraStatus(state, summary, detail);
        _statusItem.Text = summary;

        var tooltip = detail.Length == 0 ? summary : $"{summary} - {detail}";
        _notifyIcon.Text = tooltip.Length <= 63 ? tooltip : tooltip[..63];

        var color = state switch
        {
            TalvoraConnectionState.Ready => Color.FromArgb(49, 196, 112),
            TalvoraConnectionState.LocalOnly => Color.FromArgb(240, 178, 52),
            _ => Color.FromArgb(220, 72, 72),
        };

        var previous = _statusIcon;
        _statusIcon = TrayIconFactory.CreateStatusIcon(state, color);
        _notifyIcon.Icon = _statusIcon;
        previous?.Dispose();
    }

    private void ShowBalloon(
        string title,
        string text,
        ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text.Length <= 240 ? text : text[..240];
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(4000);
    }

    private void ExitTray()
    {
        _timer.Stop();
        _shutdownTimer.Stop();
        _notifyIcon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _shutdownTimer.Dispose();
            _shutdownEvent.Dispose();
            _notifyIcon.Dispose();
            _statusIcon?.Dispose();
            _operationGate.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal static class BusinessTunnelClient
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

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
                "http://127.0.0.1:7676/healthz",
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
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

    private static async Task<ProcessResult> RunClientAsync(
        BusinessConfig config,
        IReadOnlyList<string> arguments,
        string credential,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = config.TunnelClient,
            WorkingDirectory = config.StateRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["CONTROL_PLANE_API_KEY"] = credential;
        startInfo.Environment["TUNNEL_CLIENT_STATE_DIR"] = config.StateRoot;

        using var process = new Process
        {
            StartInfo = startInfo,
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("tunnel-client başlatılamadı.");
        }

        startInfo.Environment.Remove("CONTROL_PLANE_API_KEY");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new TimeoutException(
                $"tunnel-client komutu {timeout.TotalSeconds:F0} saniye içinde tamamlanmadı.");
        }

        return new ProcessResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
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

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}

internal static class NativeDpapi
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static byte[] Unprotect(byte[] encrypted)
    {
        var inputPointer = Marshal.AllocHGlobal(encrypted.Length);
        try
        {
            Marshal.Copy(encrypted, 0, inputPointer, encrypted.Length);
            var input = new DataBlob
            {
                Length = encrypted.Length,
                Data = inputPointer,
            };

            if (!CryptUnprotectData(
                    ref input,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    0,
                    out var output))
            {
                throw new InvalidOperationException(
                    $"DPAPI çözme başarısız. Win32={Marshal.GetLastWin32Error()}");
            }

            try
            {
                var result = new byte[output.Length];
                Marshal.Copy(output.Data, result, 0, output.Length);
                return result;
            }
            finally
            {
                if (output.Data != IntPtr.Zero)
                {
                    _ = LocalFree(output.Data);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(inputPointer);
        }
    }
}

internal static class TrayIconFactory
{
    public static Icon CreateStatusIcon(
        TalvoraConnectionState state,
        Color statusColor)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);

        using var applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (applicationIcon is not null)
        {
            using var brandBitmap = applicationIcon.ToBitmap();
            graphics.DrawImage(brandBitmap, new Rectangle(0, 0, 32, 32));
        }
        else
        {
            DrawBrandFallback(graphics);
        }

        DrawStatusBadge(graphics, state, statusColor);

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            _ = NativeUser32.DestroyIcon(handle);
        }
    }

    private static void DrawBrandFallback(Graphics graphics)
    {
        using var background = new SolidBrush(Color.FromArgb(9, 30, 49));
        using var border = new Pen(Color.FromArgb(0, 229, 255), 1.4f);
        using var brandPen = new Pen(Color.FromArgb(44, 160, 255), 2.6f)
        {
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
        };
        using var tBrush = new SolidBrush(Color.FromArgb(68, 220, 255));

        graphics.FillEllipse(background, 1, 1, 30, 30);
        graphics.DrawEllipse(border, 1.5f, 1.5f, 29, 29);

        var hex = new[]
        {
            new PointF(16, 5),
            new PointF(24, 10),
            new PointF(24, 20),
            new PointF(16, 27),
            new PointF(8, 20),
            new PointF(8, 10),
            new PointF(16, 5),
        };
        graphics.DrawLines(brandPen, hex);

        var t = new[]
        {
            new PointF(10, 12),
            new PointF(22, 12),
            new PointF(20, 15),
            new PointF(18, 16),
            new PointF(18, 24),
            new PointF(16, 26),
            new PointF(14, 24),
            new PointF(14, 16),
            new PointF(12, 15),
        };
        graphics.FillPolygon(tBrush, t);
    }

    private static void DrawStatusBadge(
        Graphics graphics,
        TalvoraConnectionState state,
        Color statusColor)
    {
        const float x = 20.5f;
        const float y = 20.5f;
        const float size = 10.5f;

        using var halo = new SolidBrush(Color.FromArgb(235, 5, 18, 31));
        using var fill = new SolidBrush(statusColor);
        using var outline = new Pen(Color.FromArgb(245, 235, 246, 255), 1.0f);
        graphics.FillEllipse(halo, x - 1.8f, y - 1.8f, size + 3.6f, size + 3.6f);
        graphics.FillEllipse(fill, x, y, size, size);
        graphics.DrawEllipse(outline, x, y, size, size);

        using var glyph = new Pen(Color.White, 1.35f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
        };

        switch (state)
        {
            case TalvoraConnectionState.Ready:
                graphics.DrawLines(glyph, new[]
                {
                    new PointF(23.1f, 26.0f),
                    new PointF(24.9f, 27.6f),
                    new PointF(28.5f, 23.8f),
                });
                break;

            case TalvoraConnectionState.LocalOnly:
                graphics.DrawLine(glyph, 23.4f, 25.8f, 28.4f, 25.8f);
                break;

            default:
                graphics.DrawLine(glyph, 23.8f, 23.8f, 28.1f, 28.1f);
                graphics.DrawLine(glyph, 28.1f, 23.8f, 23.8f, 28.1f);
                break;
        }
    }
}

internal static class NativeUser32
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr handle);
}

internal static class TrayLog
{
    private static readonly object Sync = new();

    private static string LogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Talvora",
            "Tray",
            "tray.log");

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            lock (Sync)
            {
                var path = LogPath;
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var line = $"{DateTimeOffset.Now:O} {message}";
                if (exception is not null)
                {
                    line += $" :: {exception.GetType().Name}: {exception.Message}";
                }

                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
        }
    }
}