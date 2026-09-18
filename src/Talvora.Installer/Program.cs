using Microsoft.Win32;
using System.Diagnostics;
using System.Drawing;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace Talvora.Installer;

internal sealed record InstallProgress(int Percent, string Message);

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal sealed record HealthSnapshot(
    string? Product,
    string? Version,
    string? SourceCommit,
    string? InstalledAtUtc,
    string? Mcp,
    int ProcessId,
    string? User,
    string? Sid,
    bool IsWindowsService);

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            return 2;
        }

        if (!IsAdministrator())
        {
            MessageBox.Show(
                "Talvora kurulumu yönetici yetkisi gerektirir.",
                "Talvora Setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return 3;
        }

        if (args.Any(arg => string.Equals(arg, "--silent", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                InstallerEngine.InstallAsync(
                    new Progress<InstallProgress>(progress =>
                        InstallerLog.Write($"{progress.Percent}% {progress.Message}")),
                    CancellationToken.None).GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception ex)
            {
                InstallerLog.Write("Silent install failed", ex);
                return 1;
            }
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new InstallerForm());
        return 0;
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}

internal sealed class InstallerForm : Form
{
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;
    private readonly Button _installButton;
    private readonly Button _closeButton;

    public InstallerForm()
    {
        Text = "Talvora Setup";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(620, 360);
        BackColor = Color.FromArgb(246, 248, 251);

        try
        {
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable))
            {
                Icon = Icon.ExtractAssociatedIcon(executable);
            }
        }
        catch
        {
        }

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 112,
            BackColor = Color.FromArgb(18, 42, 62),
        };

        var iconBox = new PictureBox
        {
            Location = new Point(28, 24),
            Size = new Size(64, 64),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = Icon?.ToBitmap(),
        };

        var title = new Label
        {
            AutoSize = true,
            Location = new Point(112, 25),
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 24, FontStyle.Bold),
            Text = "Talvora",
        };

        var subtitle = new Label
        {
            AutoSize = true,
            Location = new Point(116, 70),
            ForeColor = Color.FromArgb(188, 223, 232),
            Font = new Font("Segoe UI", 10),
            Text = "Yerel MCP + ChatGPT Business bağlantısı",
        };

        header.Controls.Add(iconBox);
        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        Controls.Add(header);

        var description = new Label
        {
            Location = new Point(32, 136),
            Size = new Size(556, 48),
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(38, 50, 61),
            Text = "Talvora Windows servisini ve sistem tepsisi uygulamasını kurar veya günceller. " +
                   "Mevcut ChatGPT Business tunnel kimliği ve Runtime API key korunur.",
        };
        Controls.Add(description);

        _statusLabel = new Label
        {
            Location = new Point(32, 205),
            Size = new Size(556, 38),
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(62, 74, 84),
            Text = "Kuruluma hazır.",
        };
        Controls.Add(_statusLabel);

        _progressBar = new ProgressBar
        {
            Location = new Point(32, 250),
            Size = new Size(556, 18),
            Minimum = 0,
            Maximum = 100,
            Value = 0,
        };
        Controls.Add(_progressBar);

        _installButton = new Button
        {
            Location = new Point(326, 296),
            Size = new Size(126, 38),
            Text = "Kur / Güncelle",
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
        };
        _installButton.Click += async (_, _) => await InstallAsync();
        Controls.Add(_installButton);

        _closeButton = new Button
        {
            Location = new Point(462, 296),
            Size = new Size(126, 38),
            Text = "Kapat",
            Font = new Font("Segoe UI", 9.5f),
        };
        _closeButton.Click += (_, _) => Close();
        Controls.Add(_closeButton);
    }

    private async Task InstallAsync()
    {
        _installButton.Enabled = false;
        _closeButton.Enabled = false;
        UseWaitCursor = true;

        var progress = new Progress<InstallProgress>(update =>
        {
            _progressBar.Value = Math.Clamp(update.Percent, 0, 100);
            _statusLabel.Text = update.Message;
        });

        try
        {
            var result = await InstallerEngine.InstallAsync(progress, CancellationToken.None);
            _progressBar.Value = 100;
            _statusLabel.Text =
                $"Talvora hazır. Servis PID: {result.ProcessId}. Sistem tepsisi uygulaması başlatıldı.";
            MessageBox.Show(
                "Talvora başarıyla kuruldu ve çalışıyor.\n\n" +
                "Saat yanındaki Talvora ikonundan ChatGPT Business bağlantısını yeniden kurabilirsiniz.",
                "Talvora hazır",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            InstallerLog.Write("Interactive install failed", ex);
            _statusLabel.Text = ex.Message;
            MessageBox.Show(
                ex.Message,
                "Talvora kurulumu başarısız",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            _installButton.Enabled = true;
            _closeButton.Enabled = true;
        }
    }
}

internal static class InstallerEngine
{
    private const string ServiceName = "Talvora";
    private const string RunValueName = "TalvoraTray";
    private const string McpUrl = "http://127.0.0.1:7676/mcp";
    private const string HealthUrl = "http://127.0.0.1:7676/healthz";

    private static readonly string[] ToolNames =
    {
        "talvora_system_info",
        "talvora_read_text",
        "talvora_write_text",
        "talvora_delete",
        "talvora_list",
        "talvora_create_directory",
        "talvora_copy",
        "talvora_move",
        "talvora_env_get",
        "talvora_env_list",
        "talvora_env_set",
        "talvora_env_delete",
        "talvora_eventlog_list",
        "talvora_eventlog_query",
        "talvora_run_process",
        "talvora_run_powershell",
        "talvora_process_list",
        "talvora_process_get",
        "talvora_process_kill",
        "talvora_service_list",
        "talvora_service_get",
        "talvora_service_start",
        "talvora_service_stop",
        "talvora_service_restart",
        "search",
        "fetch",
        "talvora_registry_create_key",
        "talvora_registry_get",
        "talvora_registry_set",
        "talvora_registry_list",
        "talvora_registry_delete_value",
        "talvora_registry_delete_key",
        "talvora_path_info",
        "talvora_file_hash",
        "talvora_find_files",
        "talvora_search_text",
        "talvora_read_bytes",
        "talvora_write_bytes",
        "talvora_replace_text",
        "talvora_http_request",
        "talvora_tcp_connections",
        "talvora_tcp_listeners",
        "talvora_wait_tcp",
        "talvora_project_discover",
        "talvora_resolve_command",
        "talvora_job_start",
        "talvora_job_get",
        "talvora_job_list",
        "talvora_job_read_output",
        "talvora_job_write_stdin",
        "talvora_job_stop",
        "talvora_job_delete",
        "talvora_git_info",
        "talvora_git_status",
        "talvora_git_diff",
        "talvora_git_log",
        "talvora_git_branches",
        "talvora_git_run",
        "talvora_read_text_range",
        "talvora_tail_text",
        "talvora_append_text",
        "talvora_json_get",
        "talvora_json_set",
        "talvora_json_delete",
        "talvora_archive_list",
        "talvora_archive_create",
        "talvora_archive_extract",
        "talvora_http_download",
        "talvora_watch_start",
        "talvora_watch_get",
        "talvora_watch_list",
        "talvora_watch_read",
        "talvora_watch_wait",
        "talvora_watch_stop",
        "talvora_choco_info",
        "talvora_choco_list",
        "talvora_choco_search",
        "talvora_choco_install",
        "talvora_choco_upgrade",
        "talvora_choco_uninstall",
        "talvora_choco_run",
        "talvora_dotnet_info",
        "talvora_dotnet_restore",
        "talvora_dotnet_build",
        "talvora_dotnet_test",
        "talvora_dotnet_publish",
        "talvora_dotnet_run",
        "talvora_node_info",
        "talvora_npm_install",
        "talvora_npm_ci",
        "talvora_npm_run_script",
        "talvora_npm_run",
        "talvora_session_list",
        "talvora_session_get",
        "talvora_user_process_start",
        "talvora_python_info",
        "talvora_python_run",
        "talvora_python_venv_create",
        "talvora_pip_install",
        "talvora_pip_run",
        "talvora_docker_info",
        "talvora_docker_ps",
        "talvora_docker_images",
        "talvora_docker_logs",
        "talvora_docker_exec",
        "talvora_docker_run",
        "talvora_docker_compose_run",
        "talvora_network_interfaces",
        "talvora_dns_lookup",
        "talvora_ping",
        "talvora_tcp_exchange",
        "talvora_tls_inspect",
        "talvora_websocket_exchange",
        "talvora_dotenv_list",
        "talvora_dotenv_get",
        "talvora_dotenv_set",
        "talvora_dotenv_delete",
        "talvora_ini_list",
        "talvora_ini_get",
        "talvora_ini_set",
        "talvora_ini_delete",
        "talvora_xml_query",
        "talvora_xml_set",
        "talvora_xml_delete",
        "talvora_test_report_summary",
        "talvora_windows_toolchain_info",
        "talvora_vs_instances",
        "talvora_windows_sdk_list",
        "talvora_vsdev_environment",
        "talvora_visual_studio_instances",
        "talvora_vs_dev_environment",
        "talvora_msbuild_info",
        "talvora_msbuild_run",
        "talvora_windows_sdk_info",
        "talvora_cmake_info",
        "talvora_cmake_run",
        "talvora_ninja_info",
        "talvora_ninja_run",
        "talvora_pe_info",
        "talvora_file_version_info",
        "talvora_http_mock_start",
        "talvora_http_mock_get",
        "talvora_http_mock_list",
        "talvora_http_mock_read",
        "talvora_http_mock_reply",
        "talvora_http_mock_stop",
        "talvora_sqlite_info",
        "talvora_sqlite_query",
        "talvora_sqlite_execute",
        "talvora_sqlite_schema",
        "talvora_sqlite_backup",
        "talvora_dev_server_start",
        "talvora_dev_server_get",
        "talvora_dev_server_list",
        "talvora_dev_server_wait",
        "talvora_dev_server_stop",
        "talvora_yaml_get",
        "talvora_yaml_set",
        "talvora_yaml_delete",
        "talvora_toml_get",
        "talvora_toml_set",
        "talvora_toml_delete",
    };

    public static async Task<HealthSnapshot> InstallAsync(
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken)
    {
        progress.Report(new InstallProgress(2, "Kurulum paketi hazırlanıyor..."));
        InstallerLog.Write("Install started");

        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "Talvora-Setup-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(tempRoot);

        string? previousServiceExecutable = null;
        string? previousTrayExecutable = null;
        var switchedService = false;

        try
        {
            ExtractPayload(tempRoot);
            var sourceCommit = File.ReadAllText(
                Path.Combine(tempRoot, "source-commit.txt")).Trim();

            if (string.IsNullOrWhiteSpace(sourceCommit))
            {
                throw new InvalidOperationException("Kurulum paketinde source commit bilgisi yok.");
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var installRoot = Path.Combine(programFiles, "Talvora");
            var versionsRoot = Path.Combine(installRoot, "Versions");
            var versionId = sourceCommit + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            var versionRoot = Path.Combine(versionsRoot, versionId);
            var serviceRoot = Path.Combine(versionRoot, "Service");
            var trayRoot = Path.Combine(versionRoot, "Tray");
            var serviceExecutable = Path.Combine(serviceRoot, "Talvora.exe");
            var trayExecutable = Path.Combine(trayRoot, "Talvora.Tray.exe");

            previousServiceExecutable = await GetExistingServiceExecutableAsync(cancellationToken);
            previousTrayExecutable = ReadTrayStartupExecutable();

            progress.Report(new InstallProgress(8, "Yeni Talvora sürümü hazırlanıyor..."));
            Directory.CreateDirectory(versionsRoot);
            Directory.CreateDirectory(serviceRoot);
            Directory.CreateDirectory(trayRoot);

            CopyTree(Path.Combine(tempRoot, "Service"), serviceRoot);
            CopyTree(Path.Combine(tempRoot, "Tray"), trayRoot);

            if (!File.Exists(serviceExecutable))
            {
                throw new FileNotFoundException(
                    "Talvora servis executable bulunamadı.",
                    serviceExecutable);
            }

            if (!File.Exists(trayExecutable))
            {
                throw new FileNotFoundException(
                    "Talvora Tray executable bulunamadı.",
                    trayExecutable);
            }

            var installedAtUtc = DateTime.UtcNow.ToString("O");
            var runtimeMetadata = new
            {
                SourceCommit = sourceCommit,
                InstalledAtUtc = installedAtUtc,
            };
            File.WriteAllText(
                Path.Combine(serviceRoot, "talvora-runtime.json"),
                JsonSerializer.Serialize(runtimeMetadata, new JsonSerializerOptions
                {
                    WriteIndented = true,
                }),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            progress.Report(new InstallProgress(22, "Eski Talvora kalıntıları temizleniyor..."));
            await RemoveLegacyInstallationAsync(cancellationToken);

            progress.Report(new InstallProgress(34, "Çalışan Talvora kontrollü olarak değiştiriliyor..."));
            await StopAndDeleteServiceAsync(ServiceName, cancellationToken);
            await StopTrayProcessesAsync(cancellationToken);

            progress.Report(new InstallProgress(50, "Windows servisi yeni sürüme bağlanıyor..."));
            await CreateServiceAsync(serviceExecutable, cancellationToken);
            switchedService = true;

            progress.Report(new InstallProgress(63, "Talvora servisi başlatılıyor..."));
            await RunScAsync(
                allowNonZero: false,
                cancellationToken,
                "start",
                ServiceName);

            var health = await WaitForHealthAsync(sourceCommit, cancellationToken);

            progress.Report(new InstallProgress(76, "Sistem tepsisi uygulaması kaydediliyor..."));
            RegisterTrayStartup(trayExecutable);
            WriteCurrentState(health, sourceCommit, installedAtUtc);
            UpsertCodexConfiguration();

            progress.Report(new InstallProgress(88, "Talvora tepsi uygulaması başlatılıyor..."));
            await StartTrayAsync(trayExecutable, cancellationToken);

            progress.Report(new InstallProgress(94, "Eski sürüm dosyaları temizleniyor..."));
            CleanupObsoleteInstallations(installRoot, versionRoot);

            progress.Report(new InstallProgress(100, "Talvora hazır."));
            InstallerLog.Write(
                $"Install succeeded. Commit={sourceCommit} PID={health.ProcessId}");
            return health;
        }
        catch
        {
            if (switchedService &&
                !string.IsNullOrWhiteSpace(previousServiceExecutable) &&
                File.Exists(previousServiceExecutable))
            {
                try
                {
                    InstallerLog.Write(
                        $"Install failed after service switch; rolling back to {previousServiceExecutable}");
                    await StopAndDeleteServiceAsync(ServiceName, cancellationToken);
                    await CreateServiceAsync(previousServiceExecutable, cancellationToken);
                    await RunScAsync(
                        allowNonZero: false,
                        cancellationToken,
                        "start",
                        ServiceName);

                    if (!string.IsNullOrWhiteSpace(previousTrayExecutable) &&
                        File.Exists(previousTrayExecutable))
                    {
                        RegisterTrayStartup(previousTrayExecutable);
                        await StartTrayAsync(previousTrayExecutable, cancellationToken);
                    }
                }
                catch (Exception rollbackError)
                {
                    InstallerLog.Write("Rollback failed", rollbackError);
                }
            }

            throw;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private static void ExtractPayload(string destination)
    {
        using var stream = Assembly
            .GetExecutingAssembly()
            .GetManifestResourceStream("Talvora.Payload.zip")
            ?? throw new InvalidOperationException("Gömülü Talvora payload bulunamadı.");

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        archive.ExtractToDirectory(destination, overwriteFiles: true);
    }

    private static async Task RemoveLegacyInstallationAsync(CancellationToken cancellationToken)
    {
        await StopAndDeleteServiceAsync("Cloudflared", cancellationToken);

        TryDeleteDirectory(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "cloudflared"));

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        TryDeleteDirectory(Path.Combine(programFiles, "Talvora", "Cloudflare"));

        try
        {
            using var legacyKey = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Talvora",
                writable: true);
            if (legacyKey is not null &&
                legacyKey.ValueCount == 0 &&
                legacyKey.SubKeyCount == 0)
            {
                Registry.CurrentUser.DeleteSubKeyTree(
                    @"SOFTWARE\Talvora",
                    throwOnMissingSubKey: false);
            }
        }
        catch
        {
        }
    }

    private static async Task StopAndDeleteServiceAsync(
        string serviceName,
        CancellationToken cancellationToken)
    {
        var query = await RunScAsync(
            allowNonZero: true,
            cancellationToken,
            "queryex",
            serviceName);

        if (query.ExitCode != 0)
        {
            return;
        }

        var processId = ParseServiceProcessId(query.StandardOutput);

        _ = await RunScAsync(
            allowNonZero: true,
            cancellationToken,
            "stop",
            serviceName);

        var stopDeadline = DateTime.UtcNow.AddSeconds(4);
        while (DateTime.UtcNow < stopDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await RunScAsync(
                allowNonZero: true,
                cancellationToken,
                "queryex",
                serviceName);

            if (status.ExitCode != 0)
            {
                return;
            }

            if (status.StandardOutput.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
            {
                _ = await RunScAsync(
                    allowNonZero: true,
                    cancellationToken,
                    "delete",
                    serviceName);
                await WaitForServiceDeletionAsync(serviceName, cancellationToken);
                return;
            }

            await Task.Delay(250, cancellationToken);
        }

        InstallerLog.Write(
            $"Service did not accept STOP; deleting registration before terminating PID={processId}.");

        _ = await RunScAsync(
            allowNonZero: true,
            cancellationToken,
            "delete",
            serviceName);

        if (processId > 0)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        await WaitForServiceDeletionAsync(serviceName, cancellationToken);
    }

    private static async Task WaitForServiceDeletionAsync(
        string serviceName,
        CancellationToken cancellationToken)
    {
        var deleteDeadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deleteDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await RunScAsync(
                allowNonZero: true,
                cancellationToken,
                "query",
                serviceName);

            if (status.ExitCode != 0)
            {
                return;
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException($"Windows service silinemedi: {serviceName}");
    }

    private static int ParseServiceProcessId(string output)
    {
        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("PID", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var separator = trimmed.IndexOf(':');
            if (separator >= 0 &&
                int.TryParse(trimmed[(separator + 1)..].Trim(), out var processId))
            {
                return processId;
            }
        }

        return 0;
    }

    private static async Task CreateServiceAsync(
        string executable,
        CancellationToken cancellationToken)
    {
        var quotedExecutable = $"\"{executable}\"";

        await RunScAsync(
            allowNonZero: false,
            cancellationToken,
            "create",
            ServiceName,
            "binPath=",
            quotedExecutable,
            "start=",
            "auto",
            "obj=",
            "LocalSystem",
            "DisplayName=",
            "Talvora");

        await RunScAsync(
            allowNonZero: false,
            cancellationToken,
            "description",
            ServiceName,
            "Talvora Local MCP - LocalSystem automatic resilient service");

        await RunScAsync(
            allowNonZero: false,
            cancellationToken,
            "failure",
            ServiceName,
            "reset=",
            "86400",
            "actions=",
            "restart/1000/restart/3000/restart/10000/restart/30000/restart/60000");

        _ = await RunScAsync(
            allowNonZero: true,
            cancellationToken,
            "failureflag",
            ServiceName,
            "1");

        _ = await RunScAsync(
            allowNonZero: true,
            cancellationToken,
            "sidtype",
            ServiceName,
            "unrestricted");
    }

    private static async Task<ProcessResult> RunScAsync(
        bool allowNonZero,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var executable = Path.Combine(systemDirectory, "sc.exe");

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo,
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("sc.exe başlatılamadı.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var result = new ProcessResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);

        if (!allowNonZero && result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Windows service işlemi başarısız ({string.Join(' ', arguments)}): " +
                $"{Collapse(result.StandardError, result.StandardOutput)}");
        }

        return result;
    }

    private static async Task<HealthSnapshot> WaitForHealthAsync(
        string sourceCommit,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3),
        };

        var deadline = DateTime.UtcNow.AddSeconds(45);
        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var json = await client.GetStringAsync(HealthUrl, cancellationToken);
                var health = JsonSerializer.Deserialize<HealthSnapshot>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    });

                if (health is not null &&
                    health.IsWindowsService &&
                    string.Equals(health.Sid, "S-1-5-18", StringComparison.Ordinal) &&
                    string.Equals(health.SourceCommit, sourceCommit, StringComparison.OrdinalIgnoreCase))
                {
                    return health;
                }

                lastError = new InvalidOperationException(
                    "Talvora health yanıtı beklenen servis/commit bilgisiyle eşleşmedi.");
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException(
            "Talvora servisi 45 saniye içinde sağlıklı hale gelmedi.",
            lastError);
    }

    private static void RegisterTrayStartup(string trayExecutable)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            writable: true);

        runKey.SetValue(
            RunValueName,
            $"\"{trayExecutable}\"",
            RegistryValueKind.String);
    }

    private static string? ReadTrayStartupExecutable()
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            writable: false);

        var value = runKey?.GetValue(RunValueName) as string;
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Trim('"');
    }

    private static Task<string?> GetExistingServiceExecutableAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var key = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Services\" + ServiceName,
            writable: false);

        var imagePath = key?.GetValue("ImagePath") as string;
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return Task.FromResult<string?>(null);
        }

        var expanded = Environment.ExpandEnvironmentVariables(imagePath).Trim();
        var executable = expanded.StartsWith('"')
            ? expanded[1..].Split('"', 2)[0]
            : expanded.Split(' ', 2)[0];

        return Task.FromResult<string?>(executable);
    }

    private static async Task StartTrayAsync(
        string trayExecutable,
        CancellationToken cancellationToken)
    {
        await StopTrayProcessesAsync(cancellationToken);

        Process.Start(new ProcessStartInfo
        {
            FileName = trayExecutable,
            UseShellExecute = true,
        });

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var process in Process.GetProcessesByName("Talvora.Tray"))
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (string.Equals(
                            path,
                            trayExecutable,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        InstallerLog.Write($"Tray started. PID={process.Id} Path={path}");
                        return;
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException("Talvora Tray 10 saniye içinde başlayamadı.");
    }

    private static async Task StopTrayProcessesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var shutdownEvent = EventWaitHandle.OpenExisting(
                @"Local\Talvora.Tray.Shutdown");
            shutdownEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }

        var gracefulDeadline = DateTime.UtcNow.AddSeconds(4);
        while (DateTime.UtcNow < gracefulDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Process.GetProcessesByName("Talvora.Tray").Length == 0)
            {
                return;
            }

            await Task.Delay(200, cancellationToken);
        }

        foreach (var process in Process.GetProcessesByName("Talvora.Tray"))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                InstallerLog.Write($"Tray force-stop failed. PID={process.Id}", ex);
            }
            finally
            {
                process.Dispose();
            }
        }

        var forcedDeadline = DateTime.UtcNow.AddSeconds(6);
        while (DateTime.UtcNow < forcedDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Process.GetProcessesByName("Talvora.Tray").Length == 0)
            {
                return;
            }

            await Task.Delay(200, cancellationToken);
        }

        throw new IOException("Talvora Tray kapatılamadı; güncelleme güvenle devam edemiyor.");
    }

    private static void CleanupObsoleteInstallations(
        string installRoot,
        string activeVersionRoot)
    {
        var legacyProgramDataService = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Talvora",
            "Service");
        DeleteDirectoryBestEffort(legacyProgramDataService);

        DeleteDirectoryBestEffort(Path.Combine(installRoot, "Service"));
        DeleteDirectoryBestEffort(Path.Combine(installRoot, "Tray"));
        DeleteDirectoryBestEffort(Path.Combine(installRoot, "Cloudflare"));

        var versionsRoot = Path.Combine(installRoot, "Versions");
        if (!Directory.Exists(versionsRoot))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(versionsRoot))
        {
            if (!string.Equals(
                    Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(activeVersionRoot).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                DeleteDirectoryBestEffort(directory);
            }
        }
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            TryDeleteDirectory(path);
        }
        catch (Exception ex)
        {
            InstallerLog.Write($"Old installation cleanup deferred until reboot: {path}", ex);
            ScheduleDirectoryDeletionOnReboot(path);
        }
    }

    private static void ScheduleDirectoryDeletionOnReboot(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(
                     path,
                     "*",
                     SearchOption.AllDirectories)
                 .OrderByDescending(item => item.Length))
        {
            _ = MoveFileEx(
                file,
                null,
                MoveFileFlags.DelayUntilReboot);
        }

        foreach (var directory in Directory.EnumerateDirectories(
                     path,
                     "*",
                     SearchOption.AllDirectories)
                 .OrderByDescending(item => item.Length))
        {
            _ = MoveFileEx(
                directory,
                null,
                MoveFileFlags.DelayUntilReboot);
        }

        if (!MoveFileEx(path, null, MoveFileFlags.DelayUntilReboot))
        {
            InstallerLog.Write(
                $"Unable to schedule legacy directory deletion. Path={path} Win32={Marshal.GetLastWin32Error()}");
        }
    }

    private static void CopyTree(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(
                Path.GetDirectoryName(target)
                ?? throw new InvalidOperationException("Hedef dosya klasörü çözümlenemedi."));
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(300);
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                Thread.Sleep(300);
            }
        }

        Directory.Delete(path, recursive: true);
    }

    private static void WriteCurrentState(
        HealthSnapshot health,
        string sourceCommit,
        string installedAtUtc)
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(localAppData, "Talvora");
        Directory.CreateDirectory(root);

        var clientConfig = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex",
            "config.toml");

        var state = new
        {
            Product = "Talvora",
            Version = health.Version ?? "3.0.0-dev",
            SourceCommit = sourceCommit,
            Service = ServiceName,
            ServiceSid = health.Sid ?? "S-1-5-18",
            health.ProcessId,
            McpUrl,
            ClientConfig = clientConfig,
            ToolCount = ToolNames.Length,
            ToolNames,
            InstalledAtUtc = installedAtUtc,
        };

        File.WriteAllText(
            Path.Combine(root, "current.json"),
            JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true,
            }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void UpsertCodexConfiguration()
    {
        var userProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        var codexRoot = Path.Combine(userProfile, ".codex");
        Directory.CreateDirectory(codexRoot);

        var path = Path.Combine(codexRoot, "config.toml");
        var original = File.Exists(path)
            ? File.ReadAllText(path)
            : string.Empty;

        const string header = "[mcp_servers.talvora_local]";
        var lines = original.Replace("\r\n", "\n").Split('\n').ToList();
        var output = new List<string>();
        var skipping = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.Equals(trimmed, header, StringComparison.Ordinal))
            {
                skipping = true;
                continue;
            }

            if (skipping &&
                trimmed.StartsWith("[", StringComparison.Ordinal) &&
                trimmed.EndsWith("]", StringComparison.Ordinal))
            {
                skipping = false;
            }

            if (!skipping)
            {
                output.Add(line);
            }
        }

        while (output.Count > 0 && string.IsNullOrWhiteSpace(output[^1]))
        {
            output.RemoveAt(output.Count - 1);
        }

        if (output.Count > 0)
        {
            output.Add(string.Empty);
        }

        output.Add(header);
        output.Add($"url = \"{McpUrl}\"");
        output.Add(string.Empty);

        File.WriteAllText(
            path,
            string.Join(Environment.NewLine, output),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string Collapse(params string[] values)
    {
        var value = string.Join(
            " ",
            values
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim()));

        return value.Length <= 500 ? value : value[..500];
    }

    [Flags]
    private enum MoveFileFlags : uint
    {
        DelayUntilReboot = 0x00000004,
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(
        string existingFileName,
        string? newFileName,
        MoveFileFlags flags);
}

internal static class InstallerLog
{
    private static readonly object Sync = new();

    private static string PathName =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Talvora",
            "Installer",
            "install.log");

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            lock (Sync)
            {
                var directory = Path.GetDirectoryName(PathName);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var line = $"{DateTimeOffset.Now:O} {message}";
                if (exception is not null)
                {
                    line += $" :: {exception.GetType().Name}: {exception.Message}";
                }

                File.AppendAllText(
                    PathName,
                    line + Environment.NewLine,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch
        {
        }
    }
}