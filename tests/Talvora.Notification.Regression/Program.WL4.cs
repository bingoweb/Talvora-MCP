using System.Text.Json;
using ModelContextProtocol.Protocol;
using Talvora;
using Talvora.Shared;

internal static partial class Program
{
    private static void AssertWl4PurposeMetadata()
    {
        const string description =
            "Bu adımın ne yaptığını ve neden yapıldığını anlatan tek satırlık Türkçe açıklama " +
            "(en çok 140 karakter); masaüstü çalışma günlüğünde 'ne' satırı olarak görünür.";

        static JsonElement InputSchema(string parameter) =>
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    [parameter] = new { type = "string" },
                },
                required = new[] { parameter },
                additionalProperties = false,
            });

        var listed = new ListToolsResult
        {
            Tools =
            [
                new Tool
                {
                    Name = "talvora_run_process",
                    InputSchema = InputSchema("executable"),
                    Annotations = new ToolAnnotations { ReadOnlyHint = false },
                },
                new Tool
                {
                    Name = "talvora_git_run",
                    InputSchema = InputSchema("repositoryPath"),
                    Annotations = new ToolAnnotations { ReadOnlyHint = false },
                },
                new Tool
                {
                    Name = "talvora_read_text",
                    InputSchema = InputSchema("path"),
                    Annotations = new ToolAnnotations { ReadOnlyHint = true },
                },
            ],
        };
        McpToolMetadataWirePolicy.Apply(listed, TalvoraMcpToolSurface.Full);

        foreach (var tool in listed.Tools)
        {
            var writable = tool.Annotations?.ReadOnlyHint != true;
            if (!tool.InputSchema.TryGetProperty("properties", out var properties))
                throw new InvalidOperationException("WL4: tools/list input properties missing.");
            var present = properties.TryGetProperty("purpose", out var property);
            if (present != writable)
                throw new InvalidOperationException("WL4: only non-read-only tools must advertise purpose: " + tool.Name);
            if (!writable)
            {
                var readOnlyArguments = new Dictionary<string, JsonElement>
                {
                    ["path"] = JsonSerializer.SerializeToElement("test"),
                    ["purpose"] = JsonSerializer.SerializeToElement("Salt okunur araçta amaç olmamalı."),
                };
                if (ToolArgumentValidator.Validate(tool.InputSchema, readOnlyArguments) is null)
                    throw new InvalidOperationException("WL4: read-only tools must reject purpose.");
                continue;
            }
            if (!property.TryGetProperty("type", out var type) ||
                type.GetString() != "string" ||
                !property.TryGetProperty("description", out var help) ||
                help.GetString() != description ||
                !property.TryGetProperty("maxLength", out var max) ||
                max.GetInt32() != 140 ||
                (tool.InputSchema.TryGetProperty("required", out var required) &&
                 required.EnumerateArray().Any(item => item.GetString() == "purpose")))
            {
                throw new InvalidOperationException("WL4: purpose must be optional, bounded and described in Turkish.");
            }

            var requiredName = tool.Name == "talvora_run_process" ? "executable" : "repositoryPath";
            var arguments = new Dictionary<string, JsonElement>
            {
                [requiredName] = JsonSerializer.SerializeToElement("test"),
                ["purpose"] = JsonSerializer.SerializeToElement("Bu komut test amacıyla çalışıyor."),
            };
            if (ToolArgumentValidator.Validate(tool.InputSchema, arguments) is not null)
                throw new InvalidOperationException("WL4: a valid purpose must be accepted.");
            if (ToolArgumentValidator.Validate(InputSchema(requiredName), arguments, allowPurpose: true) is not null ||
                ToolArgumentValidator.Validate(InputSchema(requiredName), arguments) is null)
                throw new InvalidOperationException("WL4: the runtime validator must accept purpose only on writable tools.");
            arguments["purpose"] = JsonSerializer.SerializeToElement(new string('x', 141));
            if (ToolArgumentValidator.Validate(tool.InputSchema, arguments) is null ||
                ToolArgumentValidator.Validate(InputSchema(requiredName), arguments, allowPurpose: true) is null)
                throw new InvalidOperationException("WL4: purpose longer than 140 characters must be rejected.");
            arguments["purpose"] = JsonSerializer.SerializeToElement(123);
            if (ToolArgumentValidator.Validate(tool.InputSchema, arguments) is null ||
                ToolArgumentValidator.Validate(InputSchema(requiredName), arguments, allowPurpose: true) is null)
                throw new InvalidOperationException("WL4: non-string purpose must be rejected.");
            arguments["purpose"] = JsonSerializer.SerializeToElement("Kısa Türkçe amaç.");
            arguments["invented"] = JsonSerializer.SerializeToElement("unexpected");
            if (ToolArgumentValidator.Validate(tool.InputSchema, arguments) is null)
                throw new InvalidOperationException("WL4: unrelated unknown arguments must still be rejected.");
        }
        Console.WriteLine("WL4_PURPOSE_METADATA_GREEN");
    }
}
