using System.Diagnostics;
using System.IO;
using Talvora.Shared;

namespace Talvora.Tray;

internal static partial class ControlCenterLifecycleService
{
    private static readonly TimeSpan PenpotDockerReadyTimeout =
        TimeSpan.FromSeconds(90);

    private static async Task<ManagedMcpLifecycleResult> ExecutePenpotAsync(
        ManagedMcpRegistration registration,
        ManagedMcpLifecycleOperation operation,
        CancellationToken cancellationToken)
    {
        return operation switch
        {
            ManagedMcpLifecycleOperation.Start =>
                await StartPenpotAsync(registration, cancellationToken),
            ManagedMcpLifecycleOperation.Stop =>
                await StopPenpotAsync(registration, cancellationToken),
            ManagedMcpLifecycleOperation.Restart =>
                await RestartPenpotAsync(registration, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
    }

    private static async Task<ManagedMcpLifecycleResult> StartPenpotAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken)
    {
        await RunPenpotComposeAsync(
            start: true,
            cancellationToken);

        try
        {
            _ = await ExecuteGenericAsync(
                registration,
                ManagedMcpLifecycleOperation.Start,
                cancellationToken);
        }
        catch
        {
            await StopPenpotAfterFailedStartBestEffortAsync(registration);
            throw;
        }

        return new ManagedMcpLifecycleResult(
            ManagedMcpLifecycleOperation.Start,
            "Penpot başlatıldı",
            "Penpot arayüzü ve yerel MCP bileşeni kullanıma hazırlandı.");
    }

    private static async Task<ManagedMcpLifecycleResult> StopPenpotAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken)
    {
        Exception? localFailure = null;
        try
        {
            _ = await ExecuteGenericAsync(
                registration,
                ManagedMcpLifecycleOperation.Stop,
                cancellationToken);
        }
        catch (Exception ex)
        {
            localFailure = ex;
        }

        Exception? composeFailure = null;
        try
        {
            await RunPenpotComposeAsync(
                start: false,
                cancellationToken);
        }
        catch (Exception ex)
        {
            composeFailure = ex;
        }

        if (localFailure is not null)
        {
            throw new InvalidOperationException(
                "Penpot yerel MCP bileşeni tamamen durdurulamadı.",
                localFailure);
        }

        if (composeFailure is not null)
        {
            throw new InvalidOperationException(
                "Penpot arka plan bileşenleri tamamen durdurulamadı.",
                composeFailure);
        }

        return new ManagedMcpLifecycleResult(
            ManagedMcpLifecycleOperation.Stop,
            "Penpot durduruldu",
            "Penpot arayüzü ve yerel MCP bileşeni kapatıldı; kalıcı veriler korundu.");
    }

    private static async Task<ManagedMcpLifecycleResult> RestartPenpotAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken)
    {
        _ = await StopPenpotAsync(registration, cancellationToken);
        _ = await StartPenpotAsync(registration, cancellationToken);
        return new ManagedMcpLifecycleResult(
            ManagedMcpLifecycleOperation.Restart,
            "Penpot yeniden başlatıldı",
            "Penpot arayüzü ve yerel MCP bileşeni yeniden hazırlandı.");
    }

    private static async Task StopPenpotAfterFailedStartBestEffortAsync(
        ManagedMcpRegistration registration)
    {
        try
        {
            _ = await ExecuteGenericAsync(
                registration,
                ManagedMcpLifecycleOperation.Stop,
                CancellationToken.None);
        }
        catch
        {
        }

        try
        {
            await RunPenpotComposeAsync(
                start: false,
                CancellationToken.None);
        }
        catch
        {
        }
    }

    private static async Task RunPenpotComposeAsync(
        bool start,
        CancellationToken cancellationToken)
    {
        var composePath = GetPenpotComposePath();
        if (!File.Exists(composePath))
        {
            throw new FileNotFoundException(
                "Penpot yapılandırması bulunamadı.",
                composePath);
        }

        var docker = GetDockerCliPath();
        if (!File.Exists(docker))
        {
            throw new FileNotFoundException(
                "Docker komut satırı aracı bulunamadı.",
                docker);
        }

        if (start)
        {
            await EnsureDockerDesktopReadyAsync(
                docker,
                cancellationToken);
        }
        else if (!await IsDockerReadyAsync(
                     docker,
                     cancellationToken))
        {
            return;
        }

        var arguments = start
            ? new[]
            {
                "compose",
                "-f",
                composePath,
                "up",
                "-d",
                "--remove-orphans",
            }
            : new[]
            {
                "compose",
                "-f",
                composePath,
                "down",
                "--remove-orphans",
            };

        var result = await ProcessRunner.RunAsync(
            docker,
            Path.GetDirectoryName(composePath)!,
            arguments,
            timeoutSeconds: 180,
            cancellationToken: cancellationToken,
            maxCapturedCharactersPerStream: 16_384);

        if (result.TimedOut || result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                start
                    ? "Penpot arka plan bileşenleri başlatılamadı."
                    : "Penpot arka plan bileşenleri durdurulamadı.");
        }
    }

    private static async Task EnsureDockerDesktopReadyAsync(
        string docker,
        CancellationToken cancellationToken)
    {
        if (await IsDockerReadyAsync(docker, cancellationToken))
        {
            return;
        }

        var desktop = GetDockerDesktopPath();
        if (!File.Exists(desktop))
        {
            throw new FileNotFoundException(
                "Docker Desktop bulunamadı.",
                desktop);
        }

        using var launched = Process.Start(
            new ProcessStartInfo(desktop)
            {
                UseShellExecute = true,
            });

        var deadline =
            DateTime.UtcNow.Add(PenpotDockerReadyTimeout);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await IsDockerReadyAsync(
                    docker,
                    cancellationToken))
            {
                return;
            }

            await Task.Delay(
                TimeSpan.FromSeconds(2),
                cancellationToken);
        }

        throw new TimeoutException(
            "Docker Desktop beklenen sürede hazır duruma gelmedi.");
    }

    private static async Task<bool> IsDockerReadyAsync(
        string docker,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await ProcessRunner.RunAsync(
                docker,
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile),
                new[]
                {
                    "info",
                    "--format",
                    "{{.ServerVersion}}",
                },
                timeoutSeconds: 6,
                cancellationToken: cancellationToken,
                maxCapturedCharactersPerStream: 2_048);
            return !result.TimedOut && result.ExitCode == 0;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static string GetPenpotComposePath() =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData),
            "Talvora",
            "Penpot",
            "docker-compose.yaml");

    private static string GetDockerCliPath() =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles),
            "Docker",
            "Docker",
            "resources",
            "bin",
            "docker.exe");

    private static string GetDockerDesktopPath() =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles),
            "Docker",
            "Docker",
            "Docker Desktop.exe");
}
