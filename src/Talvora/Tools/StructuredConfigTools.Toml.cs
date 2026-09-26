using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using Talvora.Shared;
using Talvora.SourceEditing;
using Tomlyn;
using Tomlyn.Model;
using YamlDotNet.Serialization;

namespace Talvora.Tools;

public static partial class StructuredConfigTools
{
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
     Description("Compatibility TOML mutator for ordinary/non-workspace files. Existing supported text encoding/BOM semantics are preserved; rewrites may normalize TOML formatting/comments. Inside recognized development workspaces, source/config mutation is rejected with SOURCE_EDIT_POLICY_VIOLATION. " + SourceEditRoutingContract.LegacyMutationRouting + " Uses RFC 6901 JSON Pointer.")]
    public static async Task<TalvoraStructuredConfigMutationResponse> TomlSet(
        string path,
        string pointer,
        string valueJson,
        bool createMissing = true,
        bool createBackup = false,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var originalEncoding =
            SourceTextCodec.ReadEncodingDescriptor(
                fullPath);
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
            originalEncoding,
            createBackup,
            "talvora_toml_set/delete",
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_toml_delete",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraStructuredConfigMutationResponse)),
     Description("Compatibility TOML delete tool for ordinary/non-workspace files. Existing supported text encoding/BOM semantics are preserved; rewrites may normalize TOML formatting/comments. Inside recognized development workspaces, source/config mutation is rejected with SOURCE_EDIT_POLICY_VIOLATION. " + SourceEditRoutingContract.LegacyMutationRouting + " Uses RFC 6901 JSON Pointer.")]
    public static async Task<TalvoraStructuredConfigMutationResponse> TomlDelete(
        string path,
        string pointer,
        bool createBackup = false,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var originalEncoding =
            SourceTextCodec.ReadEncodingDescriptor(
                fullPath);
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
            originalEncoding,
            createBackup,
            "talvora_toml_set/delete",
            cancellationToken);
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

        return TextLines.EnsureTrailingNewline(
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
}
