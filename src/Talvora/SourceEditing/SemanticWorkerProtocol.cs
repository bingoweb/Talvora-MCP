using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Talvora.SourceEditing;

internal sealed record SemanticWorkerRequest(
    string WorkspaceRoot,
    string SolutionOrProjectPath,
    string DocumentPath,
    int Line,
    int Character,
    string ExpectedRevision,
    string NewName,
    string? ProjectPath,
    string? ExpectedSymbolName,
    bool RenameOverloads,
    bool RenameInStrings,
    bool RenameInComments,
    int MaxProjects,
    int MaxDocuments,
    int MaxChangedDocuments,
    long MaxTotalChangedCharacters,
    int MaxDiagnostics,
    int TimeoutSeconds,
    bool ValidateSyntax);

internal sealed record SemanticWorkerError(
    string Code,
    string Message,
    string? Path,
    IReadOnlyDictionary<string, string>? Details);

internal sealed record SemanticWorkerResponse(
    bool Success,
    IReadOnlyList<SourceEditChangeInput> Changes,
    SemanticWorkspaceReceipt? Workspace,
    SemanticSymbolReceipt? Symbol,
    IReadOnlyList<SemanticEditDiagnostic> Diagnostics,
    SemanticGraphSnapshotTransport? Graph,
    SemanticWorkerError? Error);

internal static class SemanticWorkerHost
{
    public const string CommandArgument =
        "--semantic-worker";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public static bool IsWorkerCommand(
        string[] args) =>
        args.Length > 0 &&
        string.Equals(
            args[0],
            CommandArgument,
            StringComparison.Ordinal);

    public static string GetResponseDirectory() =>
        Path.Combine(
            Path.GetTempPath(),
            "Talvora",
            "semantic-worker");

    public static string GetResponsePath(
        string[] args)
    {
        if (args.Length != 2)
        {
            throw new InvalidOperationException(
                "Isolated semantic worker requires exactly one response-path argument.");
        }

        var responseDirectory =
            Path.GetFullPath(
                GetResponseDirectory());
        var responsePath =
            Path.GetFullPath(
                args[1]);
        if (!string.Equals(
                Path.GetDirectoryName(
                    responsePath),
                responseDirectory,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Isolated semantic worker response path is outside the managed response directory.");
        }

        return responsePath;
    }

    public static async Task RunAsync(
        Stream input,
        string responsePath,
        CancellationToken cancellationToken)
    {
        SemanticWorkerResponse response;
        try
        {
            var request =
                await JsonSerializer.DeserializeAsync<SemanticWorkerRequest>(
                    input,
                    JsonOptions,
                    cancellationToken);
            if (request is null)
            {
                response =
                    Failure(
                        SourceEditCodes.SemanticWorkerFailed,
                        $"{SourceEditCodes.SemanticWorkerFailed}: isolated semantic worker request was empty.");
            }
            else
            {
                response =
                    await RoslynSemanticEditEngine.ExecuteWorkerRequestAsync(
                        request,
                        cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is JsonException or
                NotSupportedException or
                IOException or
                UnauthorizedAccessException)
        {
            response =
                Failure(
                    SourceEditCodes.SemanticWorkerFailed,
                    $"{SourceEditCodes.SemanticWorkerFailed}: isolated semantic worker protocol failed: {ex.Message}");
        }

        Directory.CreateDirectory(
            Path.GetDirectoryName(
                responsePath)!);
        await using var output =
            new FileStream(
                responsePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous |
                FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(
            output,
            response,
            JsonOptions,
            cancellationToken);
        await output.FlushAsync(
            cancellationToken);
        output.Flush(
            flushToDisk: true);
    }

    private static SemanticWorkerResponse Failure(
        string code,
        string message) =>
        new(
            false,
            [],
            null,
            null,
            [],
            null,
            new SemanticWorkerError(
                code,
                message,
                null,
                null));
}

internal static class SemanticWorkerClient
{
    private const int WorkerExitGraceSeconds = 20;
    private const int CapturedDiagnosticCharacters = 32768;
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public static async Task<SemanticWorkerResponse> ExecuteAsync(
        SemanticWorkerRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            request);
        var responsePath =
            CreateResponsePath();
        var startInfo =
            CreateStartInfo(
                request,
                responsePath);
        using var process =
            new Process
            {
                StartInfo = startInfo,
            };
        if (!process.Start())
        {
            throw Failure(
                "The isolated semantic worker process could not be started.");
        }

        using var workerBudget =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        workerBudget.CancelAfter(
            TimeSpan.FromSeconds(
                checked(
                    request.TimeoutSeconds +
                    WorkerExitGraceSeconds)));
        var token =
            workerBudget.Token;
        var stdoutTask =
            DrainDiagnosticAsync(
                process.StandardOutput,
                token);
        var stderrTask =
            DrainDiagnosticAsync(
                process.StandardError,
                token);

        try
        {
            await JsonSerializer.SerializeAsync(
                process.StandardInput.BaseStream,
                request,
                JsonOptions,
                token);
            await process.StandardInput.BaseStream.FlushAsync(
                token);
            process.StandardInput.Close();

            await process.WaitForExitAsync(
                token);
            var standardOutput =
                await stdoutTask;
            var standardError =
                await stderrTask;
            if (process.ExitCode != 0)
            {
                throw Failure(
                    $"The isolated semantic worker exited with code {process.ExitCode}. stdout={standardOutput} stderr={standardError}");
            }

            if (!File.Exists(
                    responsePath))
            {
                throw Failure(
                    $"The isolated semantic worker returned no response file. stdout={standardOutput} stderr={standardError}");
            }

            SemanticWorkerResponse? response;
            try
            {
                await using var responseStream =
                    new FileStream(
                        responsePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 64 * 1024,
                        FileOptions.Asynchronous |
                        FileOptions.SequentialScan);
                response =
                    await JsonSerializer.DeserializeAsync<SemanticWorkerResponse>(
                        responseStream,
                        JsonOptions,
                        token);
            }
            catch (JsonException ex)
            {
                throw Failure(
                    $"The isolated semantic worker response file contained malformed JSON. stdout={standardOutput} stderr={standardError}",
                    ex);
            }

            if (response is null)
            {
                throw Failure(
                    $"The isolated semantic worker returned an empty response. stdout={standardOutput} stderr={standardError}");
            }

            return response;
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            workerBudget.IsCancellationRequested)
        {
            TryKill(
                process);
            throw new SourceEditDomainException(
                SourceEditCodes.SemanticTimeout,
                $"{SourceEditCodes.SemanticTimeout}: isolated semantic worker exceeded the absolute process budget for timeoutSeconds={request.TimeoutSeconds}.");
        }
        catch
        {
            TryKill(
                process);
            throw;
        }
        finally
        {
            if (!process.HasExited)
            {
                TryKill(
                    process);
            }

            TryDeleteResponse(
                responsePath);
        }
    }

    private static ProcessStartInfo CreateStartInfo(
        SemanticWorkerRequest request,
        string responsePath)
    {
        var assemblyPath =
            typeof(RoslynSemanticEditEngine)
                .Assembly
                .Location;
        if (string.IsNullOrWhiteSpace(
                assemblyPath))
        {
            throw Failure(
                "Talvora assembly location is unavailable for isolated semantic execution.");
        }

        var appHost =
            Path.ChangeExtension(
                assemblyPath,
                ".exe");
        ProcessStartInfo startInfo;
        if (File.Exists(
                appHost))
        {
            startInfo =
                new ProcessStartInfo(
                    appHost);
        }
        else
        {
            startInfo =
                new ProcessStartInfo(
                    "dotnet");
            startInfo.ArgumentList.Add(
                assemblyPath);
        }

        startInfo.ArgumentList.Add(
            SemanticWorkerHost.CommandArgument);
        startInfo.ArgumentList.Add(
            responsePath);
        var solutionPath =
            SourceWorkspaceClassifier.ResolveWorkspacePath(
                request.WorkspaceRoot,
                request.SolutionOrProjectPath);
        startInfo.WorkingDirectory =
            Path.GetDirectoryName(
                solutionPath) ??
            request.WorkspaceRoot;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        return startInfo;
    }

    private static async Task<string> DrainDiagnosticAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var captured =
            new StringBuilder();
        var buffer =
            new char[4096];
        while (true)
        {
            var read =
                await reader.ReadAsync(
                    buffer.AsMemory(),
                    cancellationToken);
            if (read == 0)
            {
                break;
            }

            var remaining =
                CapturedDiagnosticCharacters -
                captured.Length;
            if (remaining > 0)
            {
                captured.Append(
                    buffer,
                    0,
                    Math.Min(
                        remaining,
                        read));
            }
        }

        return captured.ToString().Trim();
    }

    private static string CreateResponsePath()
    {
        var directory =
            Path.GetFullPath(
                SemanticWorkerHost.GetResponseDirectory());
        Directory.CreateDirectory(
            directory);
        return Path.Combine(
            directory,
            $"{Guid.NewGuid():N}.json");
    }

    private static void TryDeleteResponse(
        string responsePath)
    {
        try
        {
            File.Delete(
                responsePath);
        }
        catch (Exception ex) when (
            ex is IOException or
                UnauthorizedAccessException)
        {
        }
    }

    private static void TryKill(
        Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(
                    entireProcessTree: true);
            }
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or
                System.ComponentModel.Win32Exception or
                NotSupportedException)
        {
        }
    }

    private static SourceEditDomainException Failure(
        string message,
        Exception? innerException = null) =>
        new(
            SourceEditCodes.SemanticWorkerFailed,
            $"{SourceEditCodes.SemanticWorkerFailed}: {message}",
            innerException: innerException);
}
