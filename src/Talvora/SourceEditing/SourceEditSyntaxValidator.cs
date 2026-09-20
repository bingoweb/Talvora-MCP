using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Tomlyn;
using Tomlyn.Model;
using YamlDotNet.Serialization;

namespace Talvora.SourceEditing;

internal static class SourceEditSyntaxValidator
{
    private static readonly IDeserializer YamlDeserializer =
        new DeserializerBuilder().Build();

    public static IReadOnlyList<SourceEditValidationResult> Validate(
        NormalizedSourceEditChangeSet changeSet)
    {
        var results = new List<SourceEditValidationResult>();

        foreach (var change in changeSet.Changes)
        {
            if (change.ProposedDocument is null)
            {
                continue;
            }

            var finalPath = change.DestinationFullPath ?? change.FullPath;
            if (!changeSet.ValidateSyntax)
            {
                results.Add(
                    new SourceEditValidationResult(
                        finalPath,
                        "syntax",
                        "skipped",
                        "validateSyntax=false"));
                continue;
            }

            var validator = GetValidator(finalPath);
            if (validator is null)
            {
                results.Add(
                    new SourceEditValidationResult(
                        finalPath,
                        "none",
                        "not-applicable",
                        "No maintained in-process syntax validator is registered for this file type."));
                continue;
            }

            try
            {
                validator.Value.Validate(change.ProposedDocument.Text);
                results.Add(
                    new SourceEditValidationResult(
                        finalPath,
                        validator.Value.Name,
                        "passed",
                        null));
            }
            catch (Exception ex) when (
                ex is JsonException or
                    System.Xml.XmlException or
                    YamlDotNet.Core.YamlException or
                    TomlException or
                    InvalidDataException or
                    InvalidOperationException)
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.ValidationFailed,
                    $"{validator.Value.Name} syntax validation failed: {ex.Message}",
                    finalPath,
                    innerException: ex);
            }
        }

        return results;
    }

    private static Validator? GetValidator(string path)
    {
        var fileName = Path.GetFileName(path);
        var extension = Path.GetExtension(fileName);

        if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".csx", StringComparison.OrdinalIgnoreCase))
        {
            var kind = extension.Equals(".csx", StringComparison.OrdinalIgnoreCase)
                ? SourceCodeKind.Script
                : SourceCodeKind.Regular;
            return new Validator(
                "Roslyn.CSharp",
                text =>
                {
                    var tree = CSharpSyntaxTree.ParseText(
                        text,
                        new CSharpParseOptions(LanguageVersion.Preview, kind: kind),
                        path);
                    var errors = tree.GetDiagnostics()
                        .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                        .Take(20)
                        .Select(diagnostic => diagnostic.ToString())
                        .ToArray();
                    if (errors.Length > 0)
                    {
                        throw new InvalidDataException(string.Join(Environment.NewLine, errors));
                    }
                });
        }

        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jsonc", StringComparison.OrdinalIgnoreCase))
        {
            return new Validator(
                "System.Text.Json",
                static text =>
                {
                    var options = new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip,
                    };
                    using var document = JsonDocument.Parse(text, options);
                    _ = document.RootElement.ValueKind;
                });
        }

        if (extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".config", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".props", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".targets", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".vcxproj", StringComparison.OrdinalIgnoreCase))
        {
            return new Validator(
                "System.Xml.Linq",
                static text =>
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        throw new InvalidDataException("XML document is empty.");
                    }

                    _ = XDocument.Parse(
                        text,
                        LoadOptions.PreserveWhitespace |
                        LoadOptions.SetLineInfo);
                });
        }

        if (extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".yml", StringComparison.OrdinalIgnoreCase))
        {
            return new Validator(
                "YamlDotNet",
                text => _ = YamlDeserializer.Deserialize<object?>(text));
        }

        if (extension.Equals(".toml", StringComparison.OrdinalIgnoreCase))
        {
            return new Validator(
                "Tomlyn",
                static text =>
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return;
                    }

                    _ = TomlSerializer.Deserialize<TomlTable>(text)
                        ?? throw new InvalidDataException(
                            "TOML document could not be parsed.");
                });
        }

        return null;
    }

    private readonly record struct Validator(
        string Name,
        Action<string> Validate);
}
