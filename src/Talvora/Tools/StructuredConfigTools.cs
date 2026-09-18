using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using Tomlyn;
using Tomlyn.Model;
using YamlDotNet.Serialization;

namespace Talvora.Tools;

public sealed record TalvoraStructuredConfigGetResponse(
    string Path,
    string Format,
    string Pointer,
    bool Found,
    string Kind,
    string? ValueJson);

public sealed record TalvoraStructuredConfigMutationResponse(
    string Path,
    string Format,
    string Pointer,
    bool Changed,
    string? BackupPath);

[McpServerToolType]
public static class StructuredConfigTools
{
    private static readonly IDeserializer YamlDeserializer =
        new DeserializerBuilder().Build();

    private static readonly ISerializer YamlSerializer =
        new SerializerBuilder().Build();

    [McpServerTool(
        Name = "talvora_yaml_get",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraStructuredConfigGetResponse)),
     Description("Read any accessible YAML document using RFC 6901 JSON Pointer syntax. YAML is exposed as a JSON-compatible object graph so the same pointer model used by talvora_json_get works for YAML.")]
    public static async Task<TalvoraStructuredConfigGetResponse> YamlGet(
        string path,
        string pointer = "",
        bool indented = true,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var root = await ParseYamlFileAsync(fullPath, cancellationToken);
        return BuildGetResponse(
            fullPath,
            "yaml",
            pointer,
            root,
            indented);
    }

    [McpServerTool(
        Name = "talvora_yaml_set",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraStructuredConfigMutationResponse)),
     Description("Set or create a YAML value in any accessible YAML file using RFC 6901 JSON Pointer syntax. valueJson is JSON used as the language-neutral value representation. The YAML document is normalized when rewritten; comments/formatting are not guaranteed to be preserved.")]
    public static async Task<TalvoraStructuredConfigMutationResponse> YamlSet(
        string path,
        string pointer,
        string valueJson,
        bool createMissing = true,
        bool createBackup = false,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var originalText =
            await File.ReadAllTextAsync(fullPath, cancellationToken);
        var root = ParseYaml(originalText);
        var value = JsonNode.Parse(valueJson);
        var tokens = ConfigAssetTools.ParsePointer(pointer);

        root = ConfigAssetTools.SetPointer(
            root,
            tokens,
            value,
            createMissing);

        var updatedText = SerializeYaml(root);
        return await WriteMutationAsync(
            fullPath,
            "yaml",
            pointer,
            originalText,
            updatedText,
            createBackup,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_yaml_delete",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraStructuredConfigMutationResponse)),
     Description("Delete a YAML value from any accessible YAML file using RFC 6901 JSON Pointer syntax. Missing targets are idempotent. An empty pointer replaces the document root with YAML null. Rewrites normalize YAML and may not preserve comments/formatting.")]
    public static async Task<TalvoraStructuredConfigMutationResponse> YamlDelete(
        string path,
        string pointer,
        bool createBackup = false,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var originalText =
            await File.ReadAllTextAsync(fullPath, cancellationToken);
        var root = ParseYaml(originalText);
        var tokens = ConfigAssetTools.ParsePointer(pointer);
        var changed =
            ConfigAssetTools.DeletePointer(
                ref root,
                tokens);

        if (!changed)
        {
            return new TalvoraStructuredConfigMutationResponse(
                fullPath,
                "yaml",
                pointer,
                false,
                null);
        }

        var updatedText = SerializeYaml(root);
        return await WriteMutationAsync(
            fullPath,
            "yaml",
            pointer,
            originalText,
            updatedText,
            createBackup,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_toml_get",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraStructuredConfigGetResponse)),
     Description("Read any accessible TOML document using RFC 6901 JSON Pointer syntax. TOML tables, arrays, and scalars are exposed as a JSON-compatible object graph.")]
    public static async Task<TalvoraStructuredConfigGetResponse> TomlGet(
        string path,
        string pointer = "",
        bool indented = true,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var root = await ParseTomlFileAsync(fullPath, cancellationToken);
        return BuildGetResponse(
            fullPath,
            "toml",
            pointer,
            root,
            indented);
    }

    [McpServerTool(
        Name = "talvora_toml_set",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraStructuredConfigMutationResponse)),
     Description("Set or create a TOML value in any accessible TOML file using RFC 6901 JSON Pointer syntax. valueJson is JSON used as the language-neutral value representation. TOML is normalized when rewritten; comments/formatting are not guaranteed to be preserved.")]
    public static async Task<TalvoraStructuredConfigMutationResponse> TomlSet(
        string path,
        string pointer,
        string valueJson,
        bool createMissing = true,
        bool createBackup = false,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var originalText =
            await File.ReadAllTextAsync(fullPath, cancellationToken);
        var root = ParseToml(originalText);
        var value = JsonNode.Parse(valueJson);
        var tokens = ConfigAssetTools.ParsePointer(pointer);

        root = ConfigAssetTools.SetPointer(
            root,
            tokens,
            value,
            createMissing);

        var updatedText = SerializeToml(root);
        return await WriteMutationAsync(
            fullPath,
            "toml",
            pointer,
            originalText,
            updatedText,
            createBackup,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_toml_delete",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraStructuredConfigMutationResponse)),
     Description("Delete a TOML value from any accessible TOML file using RFC 6901 JSON Pointer syntax. Missing targets are idempotent. Deleting the root produces an empty TOML table. Rewrites normalize TOML and may not preserve comments/formatting.")]
    public static async Task<TalvoraStructuredConfigMutationResponse> TomlDelete(
        string path,
        string pointer,
        bool createBackup = false,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var originalText =
            await File.ReadAllTextAsync(fullPath, cancellationToken);
        var root = ParseToml(originalText);
        var tokens = ConfigAssetTools.ParsePointer(pointer);
        var changed =
            ConfigAssetTools.DeletePointer(
                ref root,
                tokens);

        if (!changed)
        {
            return new TalvoraStructuredConfigMutationResponse(
                fullPath,
                "toml",
                pointer,
                false,
                null);
        }

        root ??= new JsonObject();
        if (root is not JsonObject)
        {
            throw new InvalidOperationException(
                "TOML document root must be an object/table.");
        }

        var updatedText = SerializeToml(root);
        return await WriteMutationAsync(
            fullPath,
            "toml",
            pointer,
            originalText,
            updatedText,
            createBackup,
            cancellationToken);
    }

    private static TalvoraStructuredConfigGetResponse BuildGetResponse(
        string path,
        string format,
        string pointer,
        JsonNode? root,
        bool indented)
    {
        var tokens = ConfigAssetTools.ParsePointer(pointer);

        if (!ConfigAssetTools.TryResolve(
                root,
                tokens,
                out var node))
        {
            return new TalvoraStructuredConfigGetResponse(
                path,
                format,
                pointer,
                false,
                "Missing",
                null);
        }

        return new TalvoraStructuredConfigGetResponse(
            path,
            format,
            pointer,
            true,
            ConfigAssetTools.GetJsonKind(node),
            ConfigAssetTools.ToJson(node, indented));
    }

    private static async Task<JsonNode?> ParseYamlFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var text =
            await File.ReadAllTextAsync(
                path,
                cancellationToken);

        return ParseYaml(text);
    }

    private static JsonNode? ParseYaml(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value =
            YamlDeserializer.Deserialize<object?>(
                text);

        return FromYamlValue(value);
    }

    private static string SerializeYaml(JsonNode? root)
    {
        var value = ToPlainValue(root);
        var text = YamlSerializer.Serialize(value);
        return EnsureTrailingNewline(text);
    }

    private static JsonNode? FromYamlValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is IDictionary dictionary)
        {
            var result = new JsonObject();
            foreach (DictionaryEntry entry in dictionary)
            {
                var key =
                    Convert.ToString(
                        entry.Key,
                        CultureInfo.InvariantCulture)
                    ?? string.Empty;

                result[key] =
                    FromYamlValue(entry.Value);
            }

            return result;
        }

        if (value is IEnumerable enumerable &&
            value is not string &&
            value is not byte[])
        {
            var result = new JsonArray();
            foreach (var item in enumerable)
            {
                result.Add(
                    FromYamlValue(item));
            }

            return result;
        }

        return value switch
        {
            bool boolean => JsonValue.Create(boolean),
            byte number => JsonValue.Create(number),
            sbyte number => JsonValue.Create(number),
            short number => JsonValue.Create(number),
            ushort number => JsonValue.Create(number),
            int number => JsonValue.Create(number),
            uint number => JsonValue.Create(number),
            long number => JsonValue.Create(number),
            ulong number => JsonValue.Create(number),
            float number => JsonValue.Create(number),
            double number => JsonValue.Create(number),
            decimal number => JsonValue.Create(number),
            DateTime dateTime => JsonValue.Create(dateTime),
            DateTimeOffset dateTimeOffset =>
                JsonValue.Create(dateTimeOffset),
            _ => JsonValue.Create(
                Convert.ToString(
                    value,
                    CultureInfo.InvariantCulture)),
        };
    }

    private static async Task<JsonNode> ParseTomlFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var text =
            await File.ReadAllTextAsync(
                path,
                cancellationToken);

        return ParseToml(text);
    }

    private static JsonNode ParseToml(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new JsonObject();
        }

        var table =
            TomlSerializer.Deserialize<TomlTable>(
                text)
            ?? throw new InvalidDataException(
                "TOML document could not be parsed.");

        return FromTomlValue(table)
            ?? new JsonObject();
    }

    private static string SerializeToml(JsonNode? root)
    {
        if (root is not JsonObject)
        {
            throw new InvalidOperationException(
                "TOML document root must be an object/table.");
        }

        var model =
            ToTomlValue(root);

        if (model is not TomlTable table)
        {
            throw new InvalidOperationException(
                "TOML document root could not be converted to a table.");
        }

        return EnsureTrailingNewline(
            TomlSerializer.Serialize(table));
    }

    private static JsonNode? FromTomlValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is TomlTable table)
        {
            var result = new JsonObject();
            foreach (var pair in table)
            {
                result[pair.Key] =
                    FromTomlValue(pair.Value);
            }

            return result;
        }

        if (value is TomlTableArray tableArray)
        {
            var result = new JsonArray();
            foreach (var item in tableArray)
            {
                result.Add(
                    FromTomlValue(item));
            }

            return result;
        }

        if (value is TomlArray array)
        {
            var result = new JsonArray();
            foreach (var item in array)
            {
                result.Add(
                    FromTomlValue(item));
            }

            return result;
        }

        return value switch
        {
            bool boolean => JsonValue.Create(boolean),
            byte number => JsonValue.Create(number),
            sbyte number => JsonValue.Create(number),
            short number => JsonValue.Create(number),
            ushort number => JsonValue.Create(number),
            int number => JsonValue.Create(number),
            uint number => JsonValue.Create(number),
            long number => JsonValue.Create(number),
            ulong number => JsonValue.Create(number),
            float number => JsonValue.Create(number),
            double number => JsonValue.Create(number),
            decimal number => JsonValue.Create(number),
            DateTime dateTime => JsonValue.Create(dateTime),
            DateTimeOffset dateTimeOffset =>
                JsonValue.Create(dateTimeOffset),
            TimeSpan timeSpan =>
                JsonValue.Create(timeSpan.ToString()),
            _ => JsonValue.Create(
                Convert.ToString(
                    value,
                    CultureInfo.InvariantCulture)),
        };
    }

    private static object ToTomlValue(JsonNode? node)
    {
        if (node is null)
        {
            throw new InvalidOperationException(
                "TOML does not support null values.");
        }

        if (node is JsonObject obj)
        {
            var table = new TomlTable();
            foreach (var pair in obj)
            {
                table[pair.Key] =
                    ToTomlValue(pair.Value);
            }

            return table;
        }

        if (node is JsonArray array)
        {
            if (array.Count > 0 &&
                array.All(item => item is JsonObject))
            {
                var tables =
                    new TomlTableArray();

                foreach (var item in array)
                {
                    tables.Add(
                        (TomlTable)ToTomlValue(item)!);
                }

                return tables;
            }

            var values = new TomlArray();
            foreach (var item in array)
            {
                values.Add(
                    ToTomlValue(item));
            }

            return values;
        }

        return ToPlainScalar(node)
            ?? throw new InvalidOperationException("TOML does not support null values.");
    }

    private static object? ToPlainValue(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonObject obj)
        {
            var dictionary =
                new Dictionary<string, object?>(
                    StringComparer.Ordinal);

            foreach (var pair in obj)
            {
                dictionary[pair.Key] =
                    ToPlainValue(pair.Value);
            }

            return dictionary;
        }

        if (node is JsonArray array)
        {
            return array
                .Select(ToPlainValue)
                .ToList();
        }

        return ToPlainScalar(node);
    }

    private static object? ToPlainScalar(JsonNode node)
    {
        using var document =
            JsonDocument.Parse(
                node.ToJsonString());
        var element =
            document.RootElement;

        return element.ValueKind switch
        {
            JsonValueKind.String =>
                element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number
                when element.TryGetInt64(out var integer) =>
                    integer,
            JsonValueKind.Number
                when element.TryGetDecimal(out var number) =>
                    number,
            JsonValueKind.Number =>
                element.GetDouble(),
            JsonValueKind.Null => null,
            _ => throw new InvalidOperationException(
                $"Unsupported scalar JSON kind: {element.ValueKind}"),
        };
    }

    private static async Task<TalvoraStructuredConfigMutationResponse>
        WriteMutationAsync(
            string path,
            string format,
            string pointer,
            string originalText,
            string updatedText,
            bool createBackup,
            CancellationToken cancellationToken)
    {
        var changed =
            !string.Equals(
                NormalizeTrailingNewline(originalText),
                NormalizeTrailingNewline(updatedText),
                StringComparison.Ordinal);

        string? backupPath = null;

        if (changed)
        {
            if (createBackup)
            {
                backupPath =
                    path + ".bak";
                File.Copy(
                    path,
                    backupPath,
                    overwrite: true);
            }

            await File.WriteAllTextAsync(
                path,
                updatedText,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
        }

        return new TalvoraStructuredConfigMutationResponse(
            path,
            format,
            pointer,
            changed,
            backupPath);
    }

    private static string EnsureTrailingNewline(string value) =>
        value.EndsWith(
            Environment.NewLine,
            StringComparison.Ordinal)
            ? value
            : value.TrimEnd('\r', '\n') +
              Environment.NewLine;

    private static string NormalizeTrailingNewline(string value) =>
        value.TrimEnd('\r', '\n');
}