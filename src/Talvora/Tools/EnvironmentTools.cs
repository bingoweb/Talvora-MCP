using System.Collections;
using System.ComponentModel;
using ModelContextProtocol.Server;

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
    [McpServerTool(
        Name = "talvora_env_get",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraEnvironmentGetResponse)),
     Description("Read one environment variable from the Talvora process, current Windows user, or local machine. No variable-name allowlist is applied.")]
    public static TalvoraEnvironmentGetResponse Get(
        string name,
        string target = "process",
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolvedTarget = ResolveTarget(target);
        var value = Environment.GetEnvironmentVariable(name, resolvedTarget);
        return new TalvoraEnvironmentGetResponse(
            value is not null,
            name,
            CanonicalTarget(resolvedTarget),
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
     Description("List environment variables from the Talvora process, current Windows user, or local machine. Optional query filters names case-insensitively. No variable-name allowlist is applied.")]
    public static TalvoraEnvironmentListResponse List(
        string target = "process",
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolvedTarget = ResolveTarget(target);
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
            CanonicalTarget(resolvedTarget),
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
     Description("Create or replace an environment variable in the Talvora process, current Windows user, or local machine. Empty-string values are preserved. No variable-name allowlist is applied.")]
    public static TalvoraEnvironmentSetResponse Set(
        string name,
        string value,
        string target = "process",
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolvedTarget = ResolveTarget(target);
        var previous = Environment.GetEnvironmentVariable(name, resolvedTarget);
        Environment.SetEnvironmentVariable(name, value, resolvedTarget);

        return new TalvoraEnvironmentSetResponse(
            true,
            name,
            CanonicalTarget(resolvedTarget),
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
     Description("Delete an environment variable from the Talvora process, current Windows user, or local machine. Missing variables are handled idempotently. No variable-name allowlist is applied.")]
    public static TalvoraEnvironmentDeleteResponse Delete(
        string name,
        string target = "process",
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolvedTarget = ResolveTarget(target);
        var previous = Environment.GetEnvironmentVariable(name, resolvedTarget);
        if (previous is not null)
        {
            Environment.SetEnvironmentVariable(name, null, resolvedTarget);
        }

        return new TalvoraEnvironmentDeleteResponse(
            previous is not null,
            name,
            CanonicalTarget(resolvedTarget),
            previous);
    }

    private static EnvironmentVariableTarget ResolveTarget(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        return target.Trim().ToLowerInvariant() switch
        {
            "process" => EnvironmentVariableTarget.Process,
            "user" => EnvironmentVariableTarget.User,
            "machine" => EnvironmentVariableTarget.Machine,
            _ => throw new ArgumentException(
                $"Unsupported environment variable target: {target}. Use process, user, or machine.",
                nameof(target)),
        };
    }

    private static string CanonicalTarget(EnvironmentVariableTarget target) =>
        target switch
        {
            EnvironmentVariableTarget.Process => "Process",
            EnvironmentVariableTarget.User => "User",
            EnvironmentVariableTarget.Machine => "Machine",
            _ => target.ToString(),
        };
}
