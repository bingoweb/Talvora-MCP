using System.Text.Json;
using System.Text.RegularExpressions;
using Talvora.Tools;

namespace Talvora;

/// <summary>
/// Names what a step works on, from the call's real arguments only (DESIGN-WORKLOG §6.1).
/// Never a command line: script, program, project, file, remote or service names.
/// </summary>
internal static partial class WorklogSubject
{
    internal const int MaximumSubjectCharacters = 48;

    internal sealed record Description(WorklogTitles Titles, string? Subject, OperationKind Kind);

    internal enum OperationKind { Program, Build, Tests, Restore, Publish, Git, PackageBuild, Deploy, Script }

    /// <summary>Repository scripts with their own approved titles (§6.1). Matched by file name.</summary>
    private static readonly Dictionary<string, WorklogTitles> KnownScripts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Invoke-QualityGate.ps1"] = new("Kalite kontrolü çalışıyor", "Kalite kontrolü geçti", "Kalite kontrolü başarısız"),
        ["Build-Windows-Installer.ps1"] = WorklogCopy.PackageBuild,
        ["Deploy-Windows-Installer.ps1"] = WorklogCopy.Deploy,
        ["Apply-DependabotPr.ps1"] = new("Paket güncellemesi hazırlanıyor", "Paket güncellemesi hazır", "Paket güncellemesi hazırlanamadı"),
        ["Get-CiStatus.ps1"] = new("GitHub denetimi okunuyor", "GitHub denetimi okundu", "GitHub denetimi okunamadı"),
        ["Get-CodexContext.ps1"] = new("Görev bağlamı hazırlanıyor", "Görev bağlamı hazır", "Görev bağlamı hazırlanamadı"),
    };

    internal static WorklogTitles ForProgram(string subject) =>
        new($"{subject} çalışıyor", $"{subject} bitti", $"{subject} hata ile bitti");

    internal static WorklogTitles ForTool(string toolTitle) =>
        new(toolTitle, $"{toolTitle} · tamamlandı", $"{toolTitle} · tamamlanamadı");

    /// <summary>"Derleniyor" + "Talvora.slnx" → "Derleniyor · Talvora.slnx".</summary>
    internal static WorklogTitles WithSubject(WorklogTitles titles, string? subject) =>
        string.IsNullOrWhiteSpace(subject)
            ? titles
            : new($"{titles.Running} · {subject}", $"{titles.Succeeded} · {subject}", $"{titles.Failed} · {subject}");

    internal static string? Shorten(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var trimmed = value.Trim();
        return trimmed.Length <= MaximumSubjectCharacters
            ? trimmed
            : trimmed[..(MaximumSubjectCharacters - 1)] + "…";
    }

    /// <summary>The first *.ps1 file named in a command text (run_powershell script, -File argument).</summary>
    internal static string? ScriptFileName(string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return null;
        }
        var match = ScriptPattern().Match(commandText);
        return match.Success ? match.Groups["name"].Value : null;
    }

    /// <summary>pwsh -File X.ps1 -Scope Full → "tam"; -Scope Fast → "hızlı" (Invoke-QualityGate only).</summary>
    private static string? ScriptMode(string scriptName, string commandText)
    {
        if (!string.Equals(scriptName, "Invoke-QualityGate.ps1", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var match = ScopePattern().Match(commandText);
        return match.Success
            ? match.Groups["scope"].Value.ToLowerInvariant() switch
            {
                "full" => "tam",
                "fast" => "hızlı",
                _ => null,
            }
            : "hızlı";
    }

    /// <summary>A script run: approved titles for known scripts, "{file} çalışıyor" otherwise.</summary>
    internal static Description ForScript(string scriptName, string commandText)
    {
        if (KnownScripts.TryGetValue(scriptName, out var known))
        {
            var kind = scriptName.ToLowerInvariant() switch
            {
                "build-windows-installer.ps1" => OperationKind.PackageBuild,
                "deploy-windows-installer.ps1" => OperationKind.Deploy,
                _ => OperationKind.Script,
            };
            var mode = ScriptMode(scriptName, commandText);
            return new(WithSubject(known, mode), scriptName, kind);
        }
        return new(ForProgram(scriptName), scriptName, OperationKind.Script);
    }

    /// <summary>Describes executable + argument vector runs (user_run, run_process, job_start, user_process_start).</summary>
    internal static Description ForExecutable(string executable, IReadOnlyList<string> arguments)
    {
        var program = Path.GetFileNameWithoutExtension(executable.Trim().Trim('"')).ToLowerInvariant();
        var commandText = string.Join(' ', arguments);
        if (program is "pwsh" or "powershell" && ScriptFileName(commandText) is { } script)
        {
            return ForScript(script, commandText);
        }

        if (program == "dotnet" && arguments.Count > 0)
        {
            var verb = arguments[0].ToLowerInvariant();
            var target = Shorten(arguments.Skip(1).FirstOrDefault(argument => !argument.StartsWith('-')) is { } path
                ? Path.GetFileName(path.Replace('\\', '/'))
                : null);
            return verb switch
            {
                "build" => new(WithSubject(WorklogCopy.Build, target), target, OperationKind.Build),
                "test" => new(WithSubject(WorklogCopy.Tests, target), target, OperationKind.Tests),
                "restore" => new(WithSubject(WorklogCopy.Restore, target), target, OperationKind.Restore),
                "publish" => new(WithSubject(WorklogCopy.Publish, target), target, OperationKind.Publish),
                _ => new(ForProgram("dotnet " + verb), "dotnet " + verb, OperationKind.Program),
            };
        }

        if (program == "git" && GitArgumentParser.GetSubcommand(arguments) is { } subcommand)
        {
            return new(WithSubject(WorklogCopy.OtherGit, subcommand), subcommand, OperationKind.Git);
        }

        if (program is "npm" or "pnpm" or "yarn" or "bun" or "npx")
        {
            var runIndex = arguments.ToList().FindIndex(argument => argument == "run");
            var name = runIndex >= 0 && runIndex + 1 < arguments.Count
                ? $"{program} {arguments[runIndex + 1]}"
                : arguments.FirstOrDefault(argument => !argument.StartsWith('-')) is { } first
                    ? $"{program} {first}"
                    : program;
            var subject = Shorten(name)!;
            return new(ForProgram(subject), subject, OperationKind.Program);
        }

        if (program is "node" or "python" or "py" or "python3" &&
            arguments.FirstOrDefault(argument => !argument.StartsWith('-')) is { } file)
        {
            var subject = Shorten(Path.GetFileName(file.Replace('\\', '/')))!;
            return new(ForProgram(subject), subject, OperationKind.Program);
        }

        var programName = Shorten(Path.GetFileName(executable.Trim().Trim('"')))!;
        return new(ForProgram(programName), programName, OperationKind.Program);
    }

    internal static string? Text(IDictionary<string, JsonElement>? arguments, string key) =>
        arguments is not null && arguments.TryGetValue(key, out var value) &&
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    internal static IReadOnlyList<string> Strings(IDictionary<string, JsonElement>? arguments, string key) =>
        arguments is not null && arguments.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!).ToArray()
            : [];

    internal static string? FileName(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Shorten(Path.GetFileName(path.Replace('\\', '/').TrimEnd('/')));

    internal static string? Host(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;

    [GeneratedRegex(@"(?<![\w.-])(?<name>[A-Za-z0-9._-]+\.ps1)\b", RegexOptions.CultureInvariant)]
    private static partial Regex ScriptPattern();

    [GeneratedRegex(@"-Scope\s+(?<scope>\w+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScopePattern();
}
