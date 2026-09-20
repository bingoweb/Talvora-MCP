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
     Description("Compatibility YAML mutator for ordinary/non-workspace files. Inside recognized development workspaces, source/config mutation is rejected with SOURCE_EDIT_POLICY_VIOLATION. " + SourceEditRoutingContract.LegacyMutationRouting + " Uses RFC 6901 JSON Pointer; rewrites may normalize YAML formatting/comments.")]
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
            "talvora_yaml_set/delete",
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_yaml_delete",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraStructuredConfigMutationResponse)),
     Description("Compatibility YAML delete tool for ordinary/non-workspace files. Inside recognized development workspaces, source/config mutation is rejected with SOURCE_EDIT_POLICY_VIOLATION. " + SourceEditRoutingContract.LegacyMutationRouting + " Uses RFC 6901 JSON Pointer; rewrites may normalize YAML formatting/comments.")]
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
            "talvora_yaml_set/delete",
            cancellationToken);
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
        return TextLines.EnsureTrailingNewline(text);
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
}
