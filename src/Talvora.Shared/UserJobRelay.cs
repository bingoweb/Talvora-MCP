using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Talvora.Shared;

public static class UserJobRelay
{
    public sealed record UserJobRequest(
        string Executable,
        string WorkingDirectory,
        IReadOnlyList<string> Arguments,
        IReadOnlyDictionary<string, string?> Environment,
        bool ClearEnvironment,
        string StandardInputPipe,
        string StandardOutputPipe,
        string StandardErrorPipe,
        string ChildProcessIdPath,
        string ExitCodePath);

    public const string RunnerScript = """
param(
    [Parameter(Mandatory = $true)][string]$RequestPath,
    [Parameter(Mandatory = $true)][string]$HelperAssemblyPath
)
$ErrorActionPreference = 'Stop'
[void][Reflection.Assembly]::LoadFrom($HelperAssemblyPath)
exit ([Talvora.Shared.UserJobRelay]::Run($RequestPath))
""";

    public static int Run(string requestPath)
    {
        UserJobRequest request;
        try
        {
            request = JsonSerializer.Deserialize<UserJobRequest>(
                File.ReadAllText(requestPath))
                ?? throw new InvalidDataException("D55: relay request is empty.");
        }
        finally
        {
            File.Delete(requestPath);
        }

        using var inputPipe = new NamedPipeClientStream(
            ".", request.StandardInputPipe, PipeDirection.In, PipeOptions.Asynchronous);
        using var outputPipe = new NamedPipeClientStream(
            ".", request.StandardOutputPipe, PipeDirection.Out, PipeOptions.Asynchronous);
        using var errorPipe = new NamedPipeClientStream(
            ".", request.StandardErrorPipe, PipeDirection.Out, PipeOptions.Asynchronous);
        inputPipe.Connect(15_000);
        outputPipe.Connect(15_000);
        errorPipe.Connect(15_000);

        string? temporaryCommandScript = null;
        try
        {
            var startInfo = LiteralProcessStart.Create(
                request.Executable, request.WorkingDirectory, request.Arguments,
                request.Environment, request.ClearEnvironment, out temporaryCommandScript);
            startInfo.RedirectStandardInput = true;
            using var process = new Process { StartInfo = startInfo };
            try
            {
                if (!process.Start())
                    throw new InvalidOperationException("D55: relay could not start the program.");
            }
            catch (Exception ex)
            {
                try
                {
                    var error = Encoding.UTF8.GetBytes(ex.Message + Environment.NewLine);
                    errorPipe.Write(error, 0, error.Length);
                    errorPipe.Flush();
                }
                catch (IOException)
                {
                }
                File.WriteAllText(request.ExitCodePath, "1");
                return 1;
            }

            File.WriteAllText(request.ChildProcessIdPath,
                process.Id.ToString(CultureInfo.InvariantCulture));

            var outputTask = Task.Run(() =>
                ForwardOutput(process.StandardOutput.BaseStream, outputPipe));
            var errorTask = Task.Run(() =>
                ForwardOutput(process.StandardError.BaseStream, errorPipe));
            _ = Task.Run(() =>
                ForwardInput(inputPipe, process.StandardInput));

            process.WaitForExit();
            _ = Task.WaitAll([outputTask, errorTask], TimeSpan.FromSeconds(5));
            var exitCode = process.ExitCode;
            File.WriteAllText(request.ExitCodePath,
                exitCode.ToString(CultureInfo.InvariantCulture));
            return exitCode;
        }
        finally
        {
            LiteralProcessStart.DeleteTemporaryCommandScript(temporaryCommandScript);
        }
    }

    private static void ForwardOutput(Stream source, Stream pipe)
    {
        var buffer = new byte[16 * 1024];
        var connected = true;
        try
        {
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (!connected)
                    continue;
                try
                {
                    pipe.Write(buffer, 0, read);
                }
                catch (IOException)
                {
                    // Keep draining the child after its caller disconnects.
                    connected = false;
                }
            }
            if (connected)
            {
                try { pipe.Flush(); }
                catch (IOException) { }
            }
        }
        catch (IOException)
        {
        }
    }

    private static void ForwardInput(Stream pipe, StreamWriter writer)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            int read;
            while ((read = pipe.Read(buffer, 0, buffer.Length)) > 0)
            {
                writer.BaseStream.Write(buffer, 0, read);
                writer.BaseStream.Flush();
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            try { writer.Close(); }
            catch (IOException) { }
        }
    }
}
