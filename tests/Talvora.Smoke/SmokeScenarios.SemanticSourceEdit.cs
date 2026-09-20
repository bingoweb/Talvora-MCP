using System.Text;
using ModelContextProtocol.Client;

internal static partial class SmokeScenarios
{
    internal static async Task RunSemanticSourceEditAsync(
        IList<McpClientTool> tools,
        IReadOnlyDictionary<string, McpClientTool> byName,
        string repositoryPath)
    {
        if (tools.Count !=
            Talvora.Shared.TalvoraToolManifest.Names.Count)
        {
            throw new InvalidOperationException(
                $"Raw tools/list count mismatch. Expected={Talvora.Shared.TalvoraToolManifest.Names.Count}; Actual={tools.Count}");
        }

        if (tools
                .Select(tool => tool.Name)
                .Distinct(StringComparer.Ordinal)
                .Count() !=
            tools.Count)
        {
            throw new InvalidOperationException(
                "Raw tools/list contains duplicate tool names.");
        }

        var semanticTool =
            byName["talvora_semantic_edit"];
        if (string.IsNullOrWhiteSpace(
                semanticTool.Description) ||
            !semanticTool.Description.Contains(
                "SPECIALIST",
                StringComparison.Ordinal) ||
            !semanticTool.Description.Contains(
                "PRIMARY/default",
                StringComparison.Ordinal) ||
            !semanticTool.Description.Contains(
                "Roslyn",
                StringComparison.Ordinal) ||
            !semanticTool.Description.Contains(
                "talvora_structural_edit",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Exact-installed semantic tool routing description is incomplete.");
        }

        var guide =
            await SmokeSupport.EnsureSuccess(
                byName["talvora_source_edit_guide"],
                new());
        if (guide.StructuredContent is not { } guideJson ||
            guideJson
                .GetProperty("primaryTool")
                .GetString() !=
            "talvora_apply_patch" ||
            guideJson
                .GetProperty("structuralTool")
                .GetString() !=
            "talvora_structural_edit" ||
            guideJson
                .GetProperty("semanticTool")
                .GetString() !=
            "talvora_semantic_edit" ||
            guideJson
                .GetProperty("exactRangeTool")
                .GetString() !=
            "talvora_apply_edits")
        {
            throw new InvalidOperationException(
                "Exact-installed routing guide does not expose the canonical four-way source-edit routing contract.");
        }

        var parent =
            Path.Combine(
                Path.GetTempPath(),
                "TalvoraSemanticLiveSmoke",
                Guid.NewGuid().ToString("N"));
        var root =
            Path.Combine(
                parent,
                "workspace");
        Directory.CreateDirectory(
            Path.Combine(
                root,
                ".git"));

        try
        {
            var libDirectory =
                Path.Combine(
                    root,
                    "Lib");
            var appDirectory =
                Path.Combine(
                    root,
                    "App");
            Directory.CreateDirectory(
                libDirectory);
            Directory.CreateDirectory(
                appDirectory);

            await File.WriteAllTextAsync(
                Path.Combine(
                    libDirectory,
                    "Lib.csproj"),
                Project());
            await File.WriteAllTextAsync(
                Path.Combine(
                    appDirectory,
                    "App.csproj"),
                Project(
                    "<ProjectReference Include=\"..\\Lib\\Lib.csproj\" />"));

            var declarationPath =
                Path.Combine(
                    libDirectory,
                    "Widget.cs");
            await File.WriteAllTextAsync(
                declarationPath,
                "namespace LiveSmoke;\r\npublic class Widget { public int Value { get; set; } }\r\n",
                new UnicodeEncoding(
                    bigEndian: false,
                    byteOrderMark: true));
            var referencePath =
                Path.Combine(
                    appDirectory,
                    "Use.cs");
            await File.WriteAllTextAsync(
                referencePath,
                "using LiveSmoke;\r\nclass Use {  Widget   value = new Widget(); }\r\n",
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false));
            await File.WriteAllTextAsync(
                Path.Combine(
                    root,
                    "LiveSmoke.slnx"),
                "<Solution>\n  <Project Path=\"Lib/Lib.csproj\" />\n  <Project Path=\"App/App.csproj\" />\n</Solution>\n",
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false));

            var read =
                await SmokeSupport.EnsureSuccess(
                    byName["talvora_read_source"],
                    new()
                    {
                        ["path"] =
                            declarationPath,
                    });
            if (read.StructuredContent is not { } readJson)
            {
                throw new InvalidOperationException(
                    "talvora_read_source returned no structured source revision.");
            }

            var revision =
                readJson
                    .GetProperty("revision")
                    .GetString()
                ?? throw new InvalidOperationException(
                    "talvora_read_source returned no revision.");
            var transactionId =
                "semantic-live-" +
                Guid.NewGuid().ToString("N");
            var arguments =
                new Dictionary<string, object?>
                {
                    ["workspaceRoot"] =
                        root,
                    ["transactionId"] =
                        transactionId,
                    ["solutionOrProjectPath"] =
                        "LiveSmoke.slnx",
                    ["documentPath"] =
                        "Lib/Widget.cs",
                    ["line"] =
                        1,
                    ["character"] =
                        13,
                    ["expectedRevision"] =
                        revision,
                    ["newName"] =
                        "RenamedWidget",
                    ["projectPath"] =
                        "Lib/Lib.csproj",
                    ["expectedSymbolName"] =
                        "Widget",
                    ["timeoutSeconds"] =
                        120,
                };

            var first =
                await SmokeSupport.EnsureSuccess(
                    semanticTool,
                    arguments);
            if (first.StructuredContent is not { } firstJson ||
                !firstJson
                    .GetProperty("success")
                    .GetBoolean() ||
                firstJson
                    .GetProperty("replayed")
                    .GetBoolean() ||
                firstJson
                    .GetProperty("symbol")
                    .GetProperty("name")
                    .GetString() !=
                "Widget" ||
                firstJson
                    .GetProperty("symbol")
                    .GetProperty("newName")
                    .GetString() !=
                "RenamedWidget")
            {
                throw new InvalidOperationException(
                    "Exact-installed semantic rename returned an invalid first receipt.");
            }

            var declarationBytes =
                await File.ReadAllBytesAsync(
                    declarationPath);
            if (declarationBytes.Length < 2 ||
                declarationBytes[0] != 0xFF ||
                declarationBytes[1] != 0xFE)
            {
                throw new InvalidOperationException(
                    "Live semantic rename failed to preserve UTF-16 LE BOM.");
            }

            var declaration =
                Encoding.Unicode.GetString(
                    declarationBytes,
                    2,
                    declarationBytes.Length - 2);
            var reference =
                await File.ReadAllTextAsync(
                    referencePath);
            if (declaration !=
                    "namespace LiveSmoke;\r\npublic class RenamedWidget { public int Value { get; set; } }\r\n" ||
                reference !=
                    "using LiveSmoke;\r\nclass Use {  RenamedWidget   value = new RenamedWidget(); }\r\n")
            {
                throw new InvalidOperationException(
                    "Live semantic rename did not preserve exact formatting/newline expectations.");
            }

            var replay =
                await SmokeSupport.EnsureSuccess(
                    semanticTool,
                    arguments);
            if (replay.StructuredContent is not { } replayJson ||
                !replayJson
                    .GetProperty("success")
                    .GetBoolean() ||
                !replayJson
                    .GetProperty("replayed")
                    .GetBoolean() ||
                !replayJson
                    .GetProperty("transaction")
                    .GetProperty("replayed")
                    .GetBoolean())
            {
                throw new InvalidOperationException(
                    "Same semantic transaction did not replay durably.");
            }

            Console.WriteLine(
                $"raw_tools={tools.Count}");
            Console.WriteLine(
                $"semantic_transaction={transactionId}");
            Console.WriteLine(
                "semantic_replayed=true");
            Console.WriteLine(
                $"repository={repositoryPath}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(
                        parent))
                {
                    Directory.Delete(
                        parent,
                        recursive: true);
                }
            }
            catch (Exception ex) when (
                ex is IOException or
                    UnauthorizedAccessException)
            {
            }
        }
    }

    private static string Project(
        string? itemGroup = null) =>
        "<Project Sdk=\"Microsoft.NET.Sdk\">" +
        "<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>" +
        (itemGroup is null
            ? string.Empty
            : "<ItemGroup>" +
              itemGroup +
              "</ItemGroup>") +
        "</Project>\n";
}
