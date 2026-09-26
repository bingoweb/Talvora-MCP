using System.Collections;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public sealed record TalvoraEnvironmentVariable(
    string Name,
    string Value);

public sealed record TalvoraEnvironmentGetResponse(
    bool Found,
    string Name,
    string Target,
    string? Value);

public sealed record TalvoraEnvironmentListResponse(
    string Target,
    IReadOnlyList<TalvoraEnvironmentVariable> Variables);

public sealed record TalvoraEnvironmentSetResponse(
    bool Applied,
    string Name,
    string Target,
    string Value,
    string? PreviousValue);

public sealed record TalvoraEnvironmentDeleteResponse(
    bool Deleted,
    string Name,
    string Target,
    string? PreviousValue);

[McpServerToolType]
public static class EnvironmentTools
{
    private enum EnvironmentScope
    {
        Process,
        InteractiveUser,
        ServiceUser,
        Machine,
    }

    private sealed record InteractiveEnvironmentRequest(
        string Operation,
        string? Name,
        string? Value);

    private sealed record InteractiveEnvironmentResult(
        bool? Found,
        string? Value,
        string? PreviousValue,
        IReadOnlyList<TalvoraEnvironmentVariable>? Variables);

    private const int InteractiveUserTimeoutSeconds = 30;
    private const string InteractiveEnvironmentPayloadName =
        "TALVORA_INTERACTIVE_ENV_PAYLOAD";

    [McpServerTool(
        Name = "talvora_env_get",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraEnvironmentGetResponse)),
     Description("Read one environment variable from the Talvora process, logged-on Windows user, Talvora service account user profile, or local machine. target=user uses the active logged-on user; target=service-user preserves direct service-account user scope. No variable-name allowlist is applied.")]
    public static async Task<TalvoraEnvironmentGetResponse> Get(
        string name,
        string target = "process",
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = ResolveScope(target);
        if (scope == EnvironmentScope.InteractiveUser)
        {
            var result =
                await RunInteractiveUserOperationAsync(
                    "get",
                    name,
                    value: null,
                    cancellationToken).ConfigureAwait(false);

            return new TalvoraEnvironmentGetResponse(
                result.Found is true,
                name,
                CanonicalTarget(scope),
                result.Value);
        }

        var resolvedTarget = ResolveDirectTarget(scope);
        var value = Environment.GetEnvironmentVariable(name, resolvedTarget);
        return new TalvoraEnvironmentGetResponse(
            value is not null,
            name,
            CanonicalTarget(scope),
            value);
    }

    [McpServerTool(
        Name = "talvora_env_list",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraEnvironmentListResponse)),
     Description("List environment variables from the Talvora process, logged-on Windows user, Talvora service account user profile, or local machine. target=user uses the active logged-on user; target=service-user preserves direct service-account user scope. Optional query filters names case-insensitively. No variable-name allowlist is applied.")]
    public static async Task<TalvoraEnvironmentListResponse> List(
        string target = "process",
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = ResolveScope(target);
        if (scope == EnvironmentScope.InteractiveUser)
        {
            var result =
                await RunInteractiveUserOperationAsync(
                    "list",
                    name: null,
                    value: null,
                    cancellationToken).ConfigureAwait(false);

            var interactiveVariables =
                result.Variables ?? [];
            return new TalvoraEnvironmentListResponse(
                CanonicalTarget(scope),
                interactiveVariables
                    .Where(item =>
                        string.IsNullOrWhiteSpace(query) ||
                        item.Name.Contains(
                            query,
                            StringComparison.OrdinalIgnoreCase))
                    .OrderBy(
                        item => item.Name,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(
                        item => item.Name,
                        StringComparer.Ordinal)
                    .ToArray());
        }

        var resolvedTarget = ResolveDirectTarget(scope);
        var variables = Environment.GetEnvironmentVariables(resolvedTarget);

        var items = new List<TalvoraEnvironmentVariable>(variables.Count);
        foreach (DictionaryEntry entry in variables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = entry.Key?.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(query) &&
                !name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            items.Add(new TalvoraEnvironmentVariable(
                name,
                entry.Value?.ToString() ?? string.Empty));
        }

        return new TalvoraEnvironmentListResponse(
            CanonicalTarget(scope),
            items
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.Ordinal)
                .ToArray());
    }

    [McpServerTool(
        Name = "talvora_env_set",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraEnvironmentSetResponse)),
     Description("Create or replace an environment variable in the Talvora process, logged-on Windows user, Talvora service account user profile, or local machine. target=user uses the active logged-on user; target=service-user preserves direct service-account user scope. Empty-string values are preserved. No variable-name allowlist is applied.")]
    public static async Task<TalvoraEnvironmentSetResponse> Set(
        string name,
        string value,
        string target = "process",
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = ResolveScope(target);
        if (scope == EnvironmentScope.InteractiveUser)
        {
            var result =
                await RunInteractiveUserOperationAsync(
                    "set",
                    name,
                    value,
                    cancellationToken).ConfigureAwait(false);

            return new TalvoraEnvironmentSetResponse(
                true,
                name,
                CanonicalTarget(scope),
                value,
                result.PreviousValue);
        }

        var resolvedTarget = ResolveDirectTarget(scope);
        var previous = Environment.GetEnvironmentVariable(name, resolvedTarget);
        Environment.SetEnvironmentVariable(name, value, resolvedTarget);

        return new TalvoraEnvironmentSetResponse(
            true,
            name,
            CanonicalTarget(scope),
            value,
            previous);
    }

    [McpServerTool(
        Name = "talvora_env_delete",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraEnvironmentDeleteResponse)),
     Description("Delete an environment variable from the Talvora process, logged-on Windows user, Talvora service account user profile, or local machine. target=user uses the active logged-on user; target=service-user preserves direct service-account user scope. Missing variables are handled idempotently. No variable-name allowlist is applied.")]
    public static async Task<TalvoraEnvironmentDeleteResponse> Delete(
        string name,
        string target = "process",
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = ResolveScope(target);
        if (scope == EnvironmentScope.InteractiveUser)
        {
            var result =
                await RunInteractiveUserOperationAsync(
                    "delete",
                    name,
                    value: null,
                    cancellationToken).ConfigureAwait(false);

            return new TalvoraEnvironmentDeleteResponse(
                result.PreviousValue is not null,
                name,
                CanonicalTarget(scope),
                result.PreviousValue);
        }

        var resolvedTarget = ResolveDirectTarget(scope);
        var previous = Environment.GetEnvironmentVariable(name, resolvedTarget);
        if (previous is not null)
        {
            Environment.SetEnvironmentVariable(name, null, resolvedTarget);
        }

        return new TalvoraEnvironmentDeleteResponse(
            previous is not null,
            name,
            CanonicalTarget(scope),
            previous);
    }

    private static EnvironmentScope ResolveScope(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        return target.Trim().ToLowerInvariant() switch
        {
            "process" => EnvironmentScope.Process,
            "user" or "interactive-user" =>
                EnvironmentScope.InteractiveUser,
            "service-user" or "serviceuser" =>
                EnvironmentScope.ServiceUser,
            "machine" => EnvironmentScope.Machine,
            _ => throw new ArgumentException(
                $"Unsupported environment variable target: {target}. Use process, user, service-user, or machine.",
                nameof(target)),
        };
    }

    private static EnvironmentVariableTarget ResolveDirectTarget(
        EnvironmentScope scope) =>
        scope switch
        {
            EnvironmentScope.Process =>
                EnvironmentVariableTarget.Process,
            EnvironmentScope.ServiceUser =>
                EnvironmentVariableTarget.User,
            EnvironmentScope.Machine =>
                EnvironmentVariableTarget.Machine,
            _ => throw new ArgumentOutOfRangeException(
                nameof(scope),
                scope,
                "Interactive user scope must run in the logged-on user session."),
        };

    private static string CanonicalTarget(EnvironmentScope scope) =>
        scope switch
        {
            EnvironmentScope.Process => "Process",
            EnvironmentScope.InteractiveUser => "User",
            EnvironmentScope.ServiceUser => "ServiceUser",
            EnvironmentScope.Machine => "Machine",
            _ => scope.ToString(),
        };

    private static async Task<InteractiveEnvironmentResult>
        RunInteractiveUserOperationAsync(
            string operation,
            string? name,
            string? value,
            CancellationToken cancellationToken)
    {
        var systemDirectory =
            Environment.GetFolderPath(
                Environment.SpecialFolder.System);
        var powershell =
            Path.Combine(
                systemDirectory,
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
        if (!File.Exists(powershell))
        {
            throw new FileNotFoundException(
                "Windows PowerShell could not be located for interactive-user environment access.",
                powershell);
        }

        var request =
            new InteractiveEnvironmentRequest(
                operation,
                name,
                value);
        var payload =
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(request)));

        var result =
            await InteractiveUserProcessRunner.RunAsync(
                powershell,
                systemDirectory,
                [
                    "-NoLogo",
                    "-NoProfile",
                    "-NonInteractive",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-Command",
                    InteractiveUserEnvironmentScript,
                ],
                new Dictionary<string, string?>
                {
                    [InteractiveEnvironmentPayloadName] =
                        payload,
                },
                timeoutSeconds:
                    InteractiveUserTimeoutSeconds,
                cancellationToken:
                    cancellationToken).ConfigureAwait(false);

        if (result.TimedOut)
        {
            throw new TimeoutException(
                "Interactive-user environment operation timed out.");
        }

        if (result.ExitCode != 0)
        {
            var detail =
                string.IsNullOrWhiteSpace(result.StandardError)
                    ? "No error detail was returned."
                    : result.StandardError.Trim();
            throw new InvalidOperationException(
                $"Interactive-user environment operation failed: {detail}");
        }

        var json =
            result.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException(
                "Interactive-user environment operation returned no result.");
        }

        return JsonSerializer.Deserialize<
                   InteractiveEnvironmentResult>(
                   json,
                   new JsonSerializerOptions
                   {
                       PropertyNameCaseInsensitive =
                           true,
                   })
               ?? throw new InvalidOperationException(
                   "Interactive-user environment operation returned invalid JSON.");
    }

    private const string InteractiveUserEnvironmentScript =
        """
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$payloadRaw = [Environment]::GetEnvironmentVariable(
    'TALVORA_INTERACTIVE_ENV_PAYLOAD',
    [EnvironmentVariableTarget]::Process)
if([string]::IsNullOrWhiteSpace($payloadRaw)) {
    throw 'Interactive environment payload is missing.'
}
$payloadJson = [Text.Encoding]::UTF8.GetString(
    [Convert]::FromBase64String($payloadRaw))
$payload = $payloadJson | ConvertFrom-Json
$target = [EnvironmentVariableTarget]::User

switch ([string]$payload.Operation) {
    'get' {
        $value = [Environment]::GetEnvironmentVariable(
            [string]$payload.Name,
            $target)
        [pscustomobject]@{
            Found = $null -ne $value
            Value = $value
            PreviousValue = $null
            Variables = $null
        } | ConvertTo-Json -Compress -Depth 4
    }
    'list' {
        $items = @(
            [Environment]::GetEnvironmentVariables($target).
                GetEnumerator() |
                ForEach-Object {
                    [pscustomobject]@{
                        Name = [string]$_.Key
                        Value = [string]$_.Value
                    }
                })
        [pscustomobject]@{
            Found = $null
            Value = $null
            PreviousValue = $null
            Variables = $items
        } | ConvertTo-Json -Compress -Depth 4
    }
    'set' {
        $previous = [Environment]::GetEnvironmentVariable(
            [string]$payload.Name,
            $target)
        [Environment]::SetEnvironmentVariable(
            [string]$payload.Name,
            [string]$payload.Value,
            $target)
        [pscustomobject]@{
            Found = $null
            Value = $null
            PreviousValue = $previous
            Variables = $null
        } | ConvertTo-Json -Compress -Depth 4
    }
    'delete' {
        $previous = [Environment]::GetEnvironmentVariable(
            [string]$payload.Name,
            $target)
        if($null -ne $previous) {
            [Environment]::SetEnvironmentVariable(
                [string]$payload.Name,
                $null,
                $target)
        }
        [pscustomobject]@{
            Found = $null
            Value = $null
            PreviousValue = $previous
            Variables = $null
        } | ConvertTo-Json -Compress -Depth 4
    }
    default {
        throw "Unsupported interactive environment operation: $($payload.Operation)"
    }
}
""";
}
