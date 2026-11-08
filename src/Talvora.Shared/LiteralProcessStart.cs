using System.Diagnostics;

namespace Talvora.Shared;

public static class LiteralProcessStart
{
    public static ProcessStartInfo Create(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?>? environment,
        bool clearEnvironment,
        out string? temporaryCommandScript)
    {
        temporaryCommandScript = null;
        var extension = Path.GetExtension(executable);
        var isCommandScript = OperatingSystem.IsWindows() &&
            (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
             extension.Equals(".bat", StringComparison.OrdinalIgnoreCase));

        if (!isCommandScript)
        {
            var direct = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = ConsoleOutputEncoding.Instance,
                StandardErrorEncoding = ConsoleOutputEncoding.Instance,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
            {
                direct.ArgumentList.Add(argument);
            }
            ApplyEnvironment(direct, environment, clearEnvironment);
            return direct;
        }

        var commandProcessor = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "cmd.exe");

        var variablePrefix = "TALVORA_" + Guid.NewGuid().ToString("N");
        temporaryCommandScript = Path.Combine(
            Path.GetTempPath(),
            $"talvora-run-{Guid.NewGuid():N}.cmd");

        var wrapper = new System.Text.StringBuilder();
        wrapper.AppendLine("@echo off");
        wrapper.AppendLine("setlocal EnableDelayedExpansion");
        wrapper.Append($"\"!{variablePrefix}_SCRIPT!\"");
        for (var index = 0; index < arguments.Count; index++)
        {
            wrapper.Append($" \"!{variablePrefix}_ARG_{index}!\"");
        }
        wrapper.AppendLine();

        File.WriteAllText(
            temporaryCommandScript,
            wrapper.ToString(),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var script = new ProcessStartInfo
        {
            FileName = commandProcessor,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = ConsoleOutputEncoding.Instance,
            StandardErrorEncoding = ConsoleOutputEncoding.Instance,
            CreateNoWindow = true,
            Arguments = "/d /v:on /s /c \"\"" + temporaryCommandScript + "\"\"",
        };

        ApplyEnvironment(script, environment, clearEnvironment);
        script.Environment[variablePrefix + "_SCRIPT"] = executable;
        for (var index = 0; index < arguments.Count; index++)
        {
            script.Environment[$"{variablePrefix}_ARG_{index}"] =
                EncodeBatchArgument(arguments[index]);
        }

        return script;
    }

    private static string EncodeBatchArgument(string value) =>
        value.Replace(
            "\"",
            "\"\"",
            StringComparison.Ordinal);

    public static void DeleteTemporaryCommandScript(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void ApplyEnvironment(ProcessStartInfo startInfo, IReadOnlyDictionary<string, string?>? environment, bool clearEnvironment)
    {
        if (clearEnvironment)
        {
            startInfo.Environment.Clear();
        }
        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                if (pair.Value is null)
                {
                    startInfo.Environment.Remove(pair.Key);
                }
                else
                {
                    startInfo.Environment[pair.Key] = pair.Value;
                }
            }
        }
    }
}
