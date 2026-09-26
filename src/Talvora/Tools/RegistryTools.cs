using System.ComponentModel;
using System.Globalization;
using Microsoft.Win32;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraRegistryValueData(
    string Name,
    string Kind,
    string? Text,
    long? Number,
    string? UnsignedNumber,
    IReadOnlyList<string>? Strings,
    string? Base64);

public sealed record TalvoraRegistryGetResponse(
    bool Found,
    string Hive,
    string Path,
    string View,
    TalvoraRegistryValueData? Value);

public sealed record TalvoraRegistryListResponse(
    bool Found,
    string Hive,
    string Path,
    string View,
    IReadOnlyList<string> SubKeys,
    IReadOnlyList<TalvoraRegistryValueData> Values,
    int Count,
    int TotalEntries,
    int ResultOffset,
    bool Truncated,
    int? NextResultOffset);

public sealed record TalvoraRegistryCreateResponse(
    bool Created,
    string Hive,
    string Path,
    string View);

public sealed record TalvoraRegistrySetResponse(
    bool Applied,
    string Hive,
    string Path,
    string View,
    string ValueName,
    string Kind);

public sealed record TalvoraRegistryDeleteResponse(
    bool Deleted,
    string Hive,
    string Path,
    string View,
    string? ValueName);

[McpServerToolType]
public static class RegistryTools
{
    internal const int AbsoluteRegistryListResults = 5_000;
    internal const long AbsoluteRegistryListResponseCharacters =
        8L * 1024 * 1024;

    [McpServerTool(
        Name = "talvora_registry_create_key",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraRegistryCreateResponse)),
     Description("Create a local Windows registry key using an explicit hive and registry view. No registry hive or path allow-list is applied.")]
    public static TalvoraRegistryCreateResponse CreateKey(
        string hive,
        string path,
        string view = "default",
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolvedHive = ResolveHive(hive);
        var resolvedView = ResolveView(view);
        var normalizedPath = NormalizePath(path);

        using var baseKey = RegistryKey.OpenBaseKey(resolvedHive.Value, resolvedView.Value);
        var existed = KeyExists(baseKey, normalizedPath);

        if (normalizedPath.Length > 0)
        {
            using var created = baseKey.CreateSubKey(normalizedPath, writable: true)
                ?? throw new InvalidOperationException($"Failed to create registry key: {CanonicalHive(resolvedHive.Value)}\\{normalizedPath}");
        }

        return new TalvoraRegistryCreateResponse(
            !existed,
            CanonicalHive(resolvedHive.Value),
            normalizedPath,
            CanonicalView(resolvedView.Value));
    }

    [McpServerTool(
        Name = "talvora_registry_get",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraRegistryGetResponse)),
     Description("Read one value from the local Windows registry. Use an empty valueName for the key's default value. ExpandString values are returned without environment expansion.")]
    public static TalvoraRegistryGetResponse GetValue(
        string hive,
        string path,
        string valueName,
        string view = "default",
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolvedHive = ResolveHive(hive);
        var resolvedView = ResolveView(view);
        var normalizedPath = NormalizePath(path);

        using var baseKey = RegistryKey.OpenBaseKey(resolvedHive.Value, resolvedView.Value);
        RegistryKey? opened = null;
        var key = baseKey;
        if (normalizedPath.Length > 0)
        {
            opened = baseKey.OpenSubKey(normalizedPath, writable: false);
            if (opened is null)
            {
                return new TalvoraRegistryGetResponse(
                    false,
                    CanonicalHive(resolvedHive.Value),
                    normalizedPath,
                    CanonicalView(resolvedView.Value),
                    null);
            }
            key = opened;
        }

        try
        {
            var actualName = key.GetValueNames()
                .FirstOrDefault(name => string.Equals(name, valueName, StringComparison.OrdinalIgnoreCase));
            if (actualName is null)
            {
                return new TalvoraRegistryGetResponse(
                    false,
                    CanonicalHive(resolvedHive.Value),
                    normalizedPath,
                    CanonicalView(resolvedView.Value),
                    null);
            }

            var kind = key.GetValueKind(actualName);
            var raw = key.GetValue(actualName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return new TalvoraRegistryGetResponse(
                true,
                CanonicalHive(resolvedHive.Value),
                normalizedPath,
                CanonicalView(resolvedView.Value),
                ConvertValue(actualName, kind, raw));
        }
        finally
        {
            opened?.Dispose();
        }
    }

    [McpServerTool(
        Name = "talvora_registry_set",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraRegistrySetResponse)),
     Description("Create or replace one local Windows registry value. Supports String, ExpandString, MultiString, DWord, QWord, Binary, and None. No registry hive or path allow-list is applied.")]
    public static TalvoraRegistrySetResponse SetValue(
        string hive,
        string path,
        string valueName,
        string kind,
        string view = "default",
        string? text = null,
        long? number = null,
        string? numeric = null,
        string[]? strings = null,
        string? base64 = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolvedHive = ResolveHive(hive);
        var resolvedView = ResolveView(view);
        var resolvedKind = ResolveKind(kind);
        var normalizedPath = NormalizePath(path);
        var value = ConvertInputValue(resolvedKind, text, number, numeric, strings, base64);

        using var baseKey = RegistryKey.OpenBaseKey(resolvedHive.Value, resolvedView.Value);
        RegistryKey? created = null;
        var key = baseKey;
        if (normalizedPath.Length > 0)
        {
            created = baseKey.CreateSubKey(normalizedPath, writable: true)
                ?? throw new InvalidOperationException($"Failed to open registry key for writing: {CanonicalHive(resolvedHive.Value)}\\{normalizedPath}");
            key = created;
        }

        try
        {
            key.SetValue(valueName, value, resolvedKind);
        }
        finally
        {
            created?.Dispose();
        }

        return new TalvoraRegistrySetResponse(
            true,
            CanonicalHive(resolvedHive.Value),
            normalizedPath,
            CanonicalView(resolvedView.Value),
            valueName,
            resolvedKind.ToString());
    }

    [McpServerTool(
        Name = "talvora_registry_list",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraRegistryListResponse)),
     Description("List subkeys and values in a local Windows registry key with finite response budgets. Names are returned in deterministic ordinal order and ExpandString values are not expanded. maxResults=0 requests the finite server maximum page; use resultOffset/nextResultOffset to continue while the key is unchanged.")]
    public static TalvoraRegistryListResponse List(
        string hive,
        string path,
        string view = "default",
        [Description("Maximum combined subkey/value entries returned; 0 requests the finite server maximum page.")] int maxResults = 500,
        [Description("Number of combined deterministic subkey/value entries to skip before returning this page.")] int resultOffset = 0,
        CancellationToken cancellationToken = default)
    {
        if (maxResults < 0 ||
            resultOffset < 0)
        {
            throw new ArgumentOutOfRangeException(
                "maxResults and resultOffset cannot be negative.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var resolvedHive = ResolveHive(hive);
        var resolvedView = ResolveView(view);
        var normalizedPath = NormalizePath(path);
        var effectiveMaxResults =
            maxResults == 0
                ? AbsoluteRegistryListResults
                : Math.Min(
                    maxResults,
                    AbsoluteRegistryListResults);

        using var baseKey = RegistryKey.OpenBaseKey(resolvedHive.Value, resolvedView.Value);
        RegistryKey? opened = null;
        var key = baseKey;
        if (normalizedPath.Length > 0)
        {
            opened = baseKey.OpenSubKey(normalizedPath, writable: false);
            if (opened is null)
            {
                return new TalvoraRegistryListResponse(
                    false,
                    CanonicalHive(resolvedHive.Value),
                    normalizedPath,
                    CanonicalView(resolvedView.Value),
                    [],
                    [],
                    0,
                    0,
                    resultOffset,
                    false,
                    null);
            }
            key = opened;
        }

        try
        {
            var allSubKeys = key.GetSubKeyNames()
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            var allValueNames = key.GetValueNames()
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var totalEntries =
                checked(
                    allSubKeys.Length +
                    allValueNames.Length);

            var subKeys = new List<string>();
            var values = new List<TalvoraRegistryValueData>();
            long responseCharacters = 0;
            var index =
                Math.Min(
                    resultOffset,
                    totalEntries);
            var returned = 0;

            while (index < totalEntries &&
                   returned < effectiveMaxResults)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (index < allSubKeys.Length)
                {
                    var subKey =
                        allSubKeys[index];
                    var entryCharacters =
                        64L + subKey.Length;
                    if (responseCharacters + entryCharacters >
                        AbsoluteRegistryListResponseCharacters)
                    {
                        break;
                    }

                    subKeys.Add(subKey);
                    responseCharacters +=
                        entryCharacters;
                }
                else
                {
                    var valueName =
                        allValueNames[
                            index -
                            allSubKeys.Length];
                    var kind =
                        key.GetValueKind(valueName);
                    var raw =
                        key.GetValue(
                            valueName,
                            null,
                            RegistryValueOptions.DoNotExpandEnvironmentNames);
                    var value =
                        ConvertValue(
                            valueName,
                            kind,
                            raw);
                    var entryCharacters =
                        EstimateValueCharacters(value);
                    if (responseCharacters + entryCharacters >
                        AbsoluteRegistryListResponseCharacters)
                    {
                        if (returned == 0)
                        {
                            throw new InvalidOperationException(
                                $"Registry value '{valueName}' exceeds the list response budget. Use talvora_registry_get for that value.");
                        }
                        break;
                    }

                    values.Add(value);
                    responseCharacters +=
                        entryCharacters;
                }

                index++;
                returned++;
            }

            var truncated =
                index < totalEntries;

            return new TalvoraRegistryListResponse(
                true,
                CanonicalHive(resolvedHive.Value),
                normalizedPath,
                CanonicalView(resolvedView.Value),
                subKeys,
                values,
                returned,
                totalEntries,
                resultOffset,
                truncated,
                truncated
                    ? index
                    : null);
        }
        finally
        {
            opened?.Dispose();
        }
    }

    [McpServerTool(
        Name = "talvora_registry_delete_value",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraRegistryDeleteResponse)),
     Description("Delete one value from a local Windows registry key. Missing keys or values are reported as Deleted=false rather than treated as errors.")]
    public static TalvoraRegistryDeleteResponse DeleteValue(
        string hive,
        string path,
        string valueName,
        string view = "default",
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolvedHive = ResolveHive(hive);
        var resolvedView = ResolveView(view);
        var normalizedPath = NormalizePath(path);

        using var baseKey = RegistryKey.OpenBaseKey(resolvedHive.Value, resolvedView.Value);
        RegistryKey? opened = null;
        var key = baseKey;
        if (normalizedPath.Length > 0)
        {
            opened = baseKey.OpenSubKey(normalizedPath, writable: true);
            if (opened is null)
            {
                return DeleteResponse(false, resolvedHive.Value, normalizedPath, resolvedView.Value, valueName);
            }
            key = opened;
        }

        try
        {
            var actualName = key.GetValueNames()
                .FirstOrDefault(name => string.Equals(name, valueName, StringComparison.OrdinalIgnoreCase));
            if (actualName is null)
            {
                return DeleteResponse(false, resolvedHive.Value, normalizedPath, resolvedView.Value, valueName);
            }

            key.DeleteValue(actualName, throwOnMissingValue: false);
            return DeleteResponse(true, resolvedHive.Value, normalizedPath, resolvedView.Value, actualName);
        }
        finally
        {
            opened?.Dispose();
        }
    }

    [McpServerTool(
        Name = "talvora_registry_delete_key",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraRegistryDeleteResponse)),
     Description("Delete a local Windows registry key. Set recursive=true to delete its complete subtree. No registry hive or path allow-list is applied.")]
    public static TalvoraRegistryDeleteResponse DeleteKey(
        string hive,
        string path,
        bool recursive = false,
        string view = "default",
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolvedHive = ResolveHive(hive);
        var resolvedView = ResolveView(view);
        var normalizedPath = NormalizePath(path);

        using var baseKey = RegistryKey.OpenBaseKey(resolvedHive.Value, resolvedView.Value);
        if (normalizedPath.Length == 0)
        {
            throw new ArgumentException("A subkey path is required when deleting a registry key.", nameof(path));
        }

        if (!KeyExists(baseKey, normalizedPath))
        {
            return DeleteResponse(false, resolvedHive.Value, normalizedPath, resolvedView.Value, null);
        }

        if (recursive)
        {
            baseKey.DeleteSubKeyTree(normalizedPath, throwOnMissingSubKey: false);
        }
        else
        {
            baseKey.DeleteSubKey(normalizedPath, throwOnMissingSubKey: false);
        }

        return DeleteResponse(true, resolvedHive.Value, normalizedPath, resolvedView.Value, null);
    }

    private static TalvoraRegistryDeleteResponse DeleteResponse(
        bool deleted,
        RegistryHive hive,
        string path,
        RegistryView view,
        string? valueName) =>
        new(deleted, CanonicalHive(hive), path, CanonicalView(view), valueName);

    private static long EstimateValueCharacters(
        TalvoraRegistryValueData value)
    {
        long total =
            128L +
            value.Name.Length +
            value.Kind.Length +
            (value.Text?.Length ?? 0) +
            (value.UnsignedNumber?.Length ?? 0) +
            (value.Base64?.Length ?? 0);

        if (value.Strings is not null)
        {
            foreach (var item in value.Strings)
            {
                total +=
                    8L +
                    item.Length;
            }
        }

        return total;
    }

    private static bool KeyExists(RegistryKey baseKey, string path)
    {
        if (path.Length == 0)
        {
            return true;
        }

        using var existing = baseKey.OpenSubKey(path, writable: false);
        return existing is not null;
    }

    private static string NormalizePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Trim().Trim('\\');
    }

    private static (RegistryHive Value, string Input) ResolveHive(string hive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hive);
        return hive.Trim().ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" or "LOCALMACHINE" => (RegistryHive.LocalMachine, hive),
            "HKCU" or "HKEY_CURRENT_USER" or "CURRENTUSER" => (RegistryHive.CurrentUser, hive),
            "HKCR" or "HKEY_CLASSES_ROOT" or "CLASSESROOT" => (RegistryHive.ClassesRoot, hive),
            "HKU" or "HKEY_USERS" or "USERS" => (RegistryHive.Users, hive),
            "HKCC" or "HKEY_CURRENT_CONFIG" or "CURRENTCONFIG" => (RegistryHive.CurrentConfig, hive),
            "HKPD" or "HKEY_PERFORMANCE_DATA" or "PERFORMANCEDATA" => (RegistryHive.PerformanceData, hive),
            _ => throw new ArgumentException($"Unsupported registry hive: {hive}", nameof(hive)),
        };
    }

    private static (RegistryView Value, string Input) ResolveView(string view)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(view);
        return view.Trim().ToLowerInvariant() switch
        {
            "default" or "native" or "registrydefault" => (RegistryView.Default, view),
            "32" or "x86" or "registry32" => (RegistryView.Registry32, view),
            "64" or "x64" or "registry64" => (RegistryView.Registry64, view),
            _ => throw new ArgumentException($"Unsupported registry view: {view}", nameof(view)),
        };
    }

    private static RegistryValueKind ResolveKind(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        return kind.Trim().ToLowerInvariant() switch
        {
            "string" or "reg_sz" => RegistryValueKind.String,
            "expandstring" or "expand_string" or "reg_expand_sz" => RegistryValueKind.ExpandString,
            "multistring" or "multi_string" or "reg_multi_sz" => RegistryValueKind.MultiString,
            "dword" or "reg_dword" => RegistryValueKind.DWord,
            "qword" or "reg_qword" => RegistryValueKind.QWord,
            "binary" or "reg_binary" => RegistryValueKind.Binary,
            "none" or "reg_none" => RegistryValueKind.None,
            _ => throw new ArgumentException($"Unsupported registry value kind: {kind}", nameof(kind)),
        };
    }

    private static object ConvertInputValue(
        RegistryValueKind kind,
        string? text,
        long? number,
        string? numeric,
        string[]? strings,
        string? base64)
    {
        return kind switch
        {
            RegistryValueKind.String or RegistryValueKind.ExpandString =>
                text ?? throw new ArgumentException("text is required for String and ExpandString values."),

            RegistryValueKind.MultiString =>
                strings ?? throw new ArgumentException("strings is required for MultiString values."),

            RegistryValueKind.DWord =>
                ParseDWord(number, numeric),

            RegistryValueKind.QWord =>
                ParseQWord(number, numeric),

            RegistryValueKind.Binary or RegistryValueKind.None =>
                string.IsNullOrWhiteSpace(base64)
                    ? []
                    : Convert.FromBase64String(base64),

            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported registry value kind."),
        };
    }

    private static int ParseDWord(long? number, string? numeric)
    {
        if (!string.IsNullOrWhiteSpace(numeric))
        {
            var raw = ParseUnsigned(numeric, uint.MaxValue);
            return unchecked((int)(uint)raw);
        }

        if (number is null)
        {
            throw new ArgumentException("number or numeric is required for DWord values.");
        }

        if (number.Value < int.MinValue || number.Value > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "DWord must fit in 32 bits.");
        }

        return number.Value >= 0
            ? unchecked((int)(uint)number.Value)
            : checked((int)number.Value);
    }

    private static long ParseQWord(long? number, string? numeric)
    {
        if (!string.IsNullOrWhiteSpace(numeric))
        {
            var raw = ParseUnsigned(numeric, ulong.MaxValue);
            return unchecked((long)raw);
        }

        return number ?? throw new ArgumentException("number or numeric is required for QWord values.");
    }

    private static ulong ParseUnsigned(string input, ulong maxValue)
    {
        var trimmed = input.Trim();
        ulong value;
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            value = ulong.Parse(trimmed.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        }
        else
        {
            value = ulong.Parse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture);
        }

        if (value > maxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(input), $"Numeric value exceeds {maxValue}.");
        }

        return value;
    }

    private static TalvoraRegistryValueData ConvertValue(
        string name,
        RegistryValueKind kind,
        object? raw)
    {
        return kind switch
        {
            RegistryValueKind.String or RegistryValueKind.ExpandString =>
                new(name, kind.ToString(), raw as string ?? string.Empty, null, null, null, null),

            RegistryValueKind.MultiString =>
                new(name, kind.ToString(), null, null, null, raw as string[] ?? [], null),

            RegistryValueKind.DWord =>
                ConvertDWord(name, raw),

            RegistryValueKind.QWord =>
                ConvertQWord(name, raw),

            RegistryValueKind.Binary or RegistryValueKind.None =>
                new(name, kind.ToString(), null, null, null, null, Convert.ToBase64String(raw as byte[] ?? [])),

            _ =>
                new(name, kind.ToString(), raw?.ToString(), null, null, null, null),
        };
    }

    private static TalvoraRegistryValueData ConvertDWord(string name, object? raw)
    {
        var signed = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
        var unsigned = unchecked((uint)signed);
        return new(name, RegistryValueKind.DWord.ToString(), null, signed, unsigned.ToString(CultureInfo.InvariantCulture), null, null);
    }

    private static TalvoraRegistryValueData ConvertQWord(string name, object? raw)
    {
        var signed = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
        var unsigned = unchecked((ulong)signed);
        return new(name, RegistryValueKind.QWord.ToString(), null, signed, unsigned.ToString(CultureInfo.InvariantCulture), null, null);
    }

    private static string CanonicalHive(RegistryHive hive) => hive switch
    {
        RegistryHive.LocalMachine => "HKLM",
        RegistryHive.CurrentUser => "HKCU",
        RegistryHive.ClassesRoot => "HKCR",
        RegistryHive.Users => "HKU",
        RegistryHive.CurrentConfig => "HKCC",
        RegistryHive.PerformanceData => "HKPD",
        _ => hive.ToString(),
    };

    private static string CanonicalView(RegistryView view) => view switch
    {
        RegistryView.Registry32 => "registry32",
        RegistryView.Registry64 => "registry64",
        _ => "default",
    };
}
