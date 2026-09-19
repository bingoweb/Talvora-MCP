using System.ComponentModel;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public static partial class ConfigAssetTools
{
private static readonly JsonSerializerOptions IndentedJsonOptions =
        new(JsonSerializerOptions.Default) { WriteIndented = true };

    [McpServerTool(
        Name = "talvora_json_get",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraJsonGetResponse)),
     Description("Read a JSON value from any accessible JSON file using RFC 6901 JSON Pointer syntax. An empty pointer returns the complete document.")]
    public static async Task<TalvoraJsonGetResponse> JsonGet(
        string path,
        string pointer = "",
        bool indented = true,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var root = await ParseJsonFileAsync(fullPath, cancellationToken);
        var tokens = ParsePointer(pointer);

        if (!TryResolve(root, tokens, out var node))
        {
            return new TalvoraJsonGetResponse(
                fullPath,
                pointer,
                false,
                "Missing",
                null);
        }

        return new TalvoraJsonGetResponse(
            fullPath,
            pointer,
            true,
            GetJsonKind(node),
            ToJson(node, indented));
    }

    [McpServerTool(
        Name = "talvora_json_set",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraJsonMutationResponse)),
     Description("Set or create a JSON value in any accessible JSON file using RFC 6901 JSON Pointer syntax. valueJson must itself be valid JSON. createMissing can create intermediate object/array nodes. No path allow-list is applied.")]
    public static async Task<TalvoraJsonMutationResponse> JsonSet(
        string path,
        string pointer,
        string valueJson,
        bool createMissing = true,
        bool indented = true,
        bool createBackup = false,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var originalText = await File.ReadAllTextAsync(fullPath, cancellationToken);
        var root = JsonNode.Parse(originalText);
        var value = JsonNode.Parse(valueJson);
        var tokens = ParsePointer(pointer);

        root = SetPointer(root, tokens, value, createMissing);

        var updatedText = ToJson(root, indented);
        var changed = !string.Equals(
            TextLines.NormalizeTrailingNewline(originalText),
            TextLines.NormalizeTrailingNewline(updatedText),
            StringComparison.Ordinal);

        string? backupPath = null;
        if (changed)
        {
            backupPath = await AtomicFile.WriteAllTextAsync(
                fullPath,
                updatedText,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                createBackup,
                cancellationToken);
        }

        return new TalvoraJsonMutationResponse(
            fullPath,
            pointer,
            changed,
            backupPath);
    }

    [McpServerTool(
        Name = "talvora_json_delete",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraJsonMutationResponse)),
     Description("Delete a JSON value from any accessible JSON file using RFC 6901 JSON Pointer syntax. An empty pointer replaces the complete document with JSON null. Missing targets are handled idempotently.")]
    public static async Task<TalvoraJsonMutationResponse> JsonDelete(
        string path,
        string pointer,
        bool indented = true,
        bool createBackup = false,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var originalText = await File.ReadAllTextAsync(fullPath, cancellationToken);
        var root = JsonNode.Parse(originalText);
        var tokens = ParsePointer(pointer);
        var changed = DeletePointer(ref root, tokens);

        string? backupPath = null;
        if (changed)
        {
            backupPath = await AtomicFile.WriteAllTextAsync(
                fullPath,
                ToJson(root, indented),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                createBackup,
                cancellationToken);
        }

        return new TalvoraJsonMutationResponse(
            fullPath,
            pointer,
            changed,
            backupPath);
    }

    private static async Task<JsonNode?> ParseJsonFileAsync(
        string fullPath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            useAsync: true);

        return await JsonNode.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
    }

    internal static string[] ParsePointer(string pointer)
    {
        if (pointer is null)
        {
            throw new ArgumentNullException(nameof(pointer));
        }

        if (pointer.Length == 0)
        {
            return [];
        }

        if (!pointer.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "JSON Pointer must be empty or begin with '/'.",
                nameof(pointer));
        }

        return pointer[1..]
            .Split('/')
            .Select(token => token.Replace("~1", "/").Replace("~0", "~"))
            .ToArray();
    }

    internal static bool TryResolve(
        JsonNode? root,
        IReadOnlyList<string> tokens,
        out JsonNode? node)
    {
        node = root;
        if (tokens.Count == 0)
        {
            return true;
        }

        for (var i = 0; i < tokens.Count; i++)
        {
            if (node is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(tokens[i], out node))
                {
                    return false;
                }
            }
            else if (node is JsonArray array)
            {
                if (!TryParseArrayIndex(tokens[i], out var index) ||
                    index >= array.Count)
                {
                    return false;
                }

                node = array[index];
            }
            else
            {
                return false;
            }

            if (node is null && i < tokens.Count - 1)
            {
                return false;
            }
        }

        return true;
    }

    internal static JsonNode? SetPointer(
        JsonNode? root,
        IReadOnlyList<string> tokens,
        JsonNode? value,
        bool createMissing)
    {
        if (tokens.Count == 0)
        {
            return value;
        }

        root ??= CreateContainerForToken(tokens[0]);

        JsonNode current = root;
        for (var i = 0; i < tokens.Count - 1; i++)
        {
            var token = tokens[i];
            var nextToken = tokens[i + 1];

            if (current is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(token, out var child) || child is null)
                {
                    if (!createMissing)
                    {
                        throw new KeyNotFoundException(
                            $"JSON Pointer segment was not found: {token}");
                    }

                    child = CreateContainerForToken(nextToken);
                    obj[token] = child;
                }

                current = child;
            }
            else if (current is JsonArray array)
            {
                if (!TryParseArrayIndex(token, out var index))
                {
                    throw new InvalidOperationException(
                        $"JSON Pointer array segment is not a non-negative integer: {token}");
                }

                if (index >= array.Count)
                {
                    if (!createMissing)
                    {
                        throw new InvalidOperationException(
                            $"JSON Pointer array index is outside the array: {index}");
                    }

                    while (array.Count <= index)
                    {
                        array.Add(null);
                    }
                }

                var child = array[index];
                if (child is null)
                {
                    if (!createMissing)
                    {
                        throw new KeyNotFoundException(
                            $"JSON Pointer segment resolved to null: {token}");
                    }

                    child = CreateContainerForToken(nextToken);
                    array[index] = child;
                }

                current = child;
            }
            else
            {
                throw new InvalidOperationException(
                    $"JSON Pointer cannot traverse through scalar value at segment: {token}");
            }
        }

        var finalToken = tokens[^1];

        if (current is JsonObject finalObject)
        {
            finalObject[finalToken] = value;
            return root;
        }

        if (current is JsonArray finalArray)
        {
            if (string.Equals(finalToken, "-", StringComparison.Ordinal))
            {
                finalArray.Add(value);
                return root;
            }

            if (!TryParseArrayIndex(finalToken, out var index))
            {
                throw new InvalidOperationException(
                    $"JSON Pointer array segment is not a non-negative integer or '-': {finalToken}");
            }

            if (index < finalArray.Count)
            {
                finalArray[index] = value;
                return root;
            }

            if (!createMissing)
            {
                throw new InvalidOperationException(
                    $"JSON Pointer array index is outside the array: {index}");
            }

            while (finalArray.Count < index)
            {
                finalArray.Add(null);
            }

            finalArray.Add(value);
            return root;
        }

        throw new InvalidOperationException(
            "JSON Pointer parent is a scalar value.");
    }

    internal static bool DeletePointer(
        ref JsonNode? root,
        IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
        {
            var changed = root is not null;
            root = null;
            return changed;
        }

        if (root is null)
        {
            return false;
        }

        JsonNode current = root;
        for (var i = 0; i < tokens.Count - 1; i++)
        {
            var token = tokens[i];

            if (current is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(token, out var child) || child is null)
                {
                    return false;
                }
                current = child;
            }
            else if (current is JsonArray array)
            {
                if (!int.TryParse(token, out var index) ||
                    index < 0 ||
                    index >= array.Count ||
                    array[index] is not { } child)
                {
                    return false;
                }
                current = child;
            }
            else
            {
                return false;
            }
        }

        var finalToken = tokens[^1];
        if (current is JsonObject finalObject)
        {
            return finalObject.Remove(finalToken);
        }

        if (current is JsonArray finalArray &&
            TryParseArrayIndex(finalToken, out var finalIndex) &&
            finalIndex < finalArray.Count)
        {
            finalArray.RemoveAt(finalIndex);
            return true;
        }

        return false;
    }

    private static JsonNode CreateContainerForToken(string token) =>
        string.Equals(token, "-", StringComparison.Ordinal) ||
        TryParseArrayIndex(token, out _)
            ? new JsonArray()
            : new JsonObject();

    private static bool TryParseArrayIndex(string token, out int index) =>
        int.TryParse(
            token,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out index);

    internal static string GetJsonKind(JsonNode? node) =>
        node switch
        {
            null => "Null",
            JsonObject => "Object",
            JsonArray => "Array",
            JsonValue value => GetJsonValueKind(value),
            _ => "Unknown",
        };

    private static string GetJsonValueKind(JsonValue value)
    {
        using var document = JsonDocument.Parse(value.ToJsonString());
        return document.RootElement.ValueKind.ToString();
    }

    internal static string ToJson(JsonNode? node, bool indented) =>
        node?.ToJsonString(
            indented ? IndentedJsonOptions : JsonSerializerOptions.Default)
        ?? "null";
}
