using System.Text;
using Talvora.SourceEditing;

internal static partial class SourceEditRegressionRunner
{
    private static async Task SemanticSolutionRenameReplayAsync()
    {
        await using var fixture =
            await TestWorkspace.CreateAsync();
        await WriteTwoProjectSemanticSolutionAsync(
            fixture);

        var anchor =
            fixture.PathInWorkspace(
                "Lib/Widget.cs");
        var anchorRead =
            await SourceEditRuntime.Engine.ReadSourceAsync(
                anchor,
                1,
                0,
                CancellationToken.None);
        var transactionId =
            NewTransactionId();
        var first =
            await RenameAsync(
                fixture,
                transactionId,
                "Demo.slnx",
                "Lib/Widget.cs",
                1,
                13,
                anchorRead.Revision,
                "RenamedWidget",
                projectPath: "Lib/Lib.csproj",
                expectedSymbolName: "Widget");

        Assert(
            first.Success,
            first.Transaction.Error?.Message ??
            "Solution-aware semantic rename failed.");
        Assert(
            first.Symbol is not null &&
            first.Symbol.Name == "Widget" &&
            first.Symbol.NewName == "RenamedWidget",
            "Semantic symbol receipt was not populated correctly.");
        Assert(
            first.Workspace is not null &&
            first.Workspace.ProjectCount == 2 &&
            !string.IsNullOrWhiteSpace(
                first.Workspace.MsBuildPath),
            "Semantic workspace/MSBuild receipt was not populated.");

        var expectedLib =
            "namespace Demo;\r\npublic class RenamedWidget { public int X { get; set; } }\r\n";
        var expectedApp =
            "using Demo;\r\nclass Use {  RenamedWidget   value = new RenamedWidget(); }\r\n";
        var libBytes =
            await File.ReadAllBytesAsync(
                anchor);
        Assert(
            libBytes.Length >= 2 &&
            libBytes[0] == 0xFF &&
            libBytes[1] == 0xFE,
            "Semantic rename did not preserve the UTF-16 LE BOM.");
        AssertEqual(
            expectedLib,
            Encoding.Unicode.GetString(
                libBytes,
                2,
                libBytes.Length - 2),
            "Semantic rename changed unrelated formatting/newlines in the declaration document.");
        AssertEqual(
            expectedApp,
            await File.ReadAllTextAsync(
                fixture.PathInWorkspace(
                    "App/Use.cs")),
            "Semantic rename changed unrelated formatting/newlines in the reference document.");

        var replay =
            await RenameAsync(
                fixture,
                transactionId,
                "Demo.slnx",
                "Lib/Widget.cs",
                1,
                13,
                anchorRead.Revision,
                "RenamedWidget",
                projectPath: "Lib/Lib.csproj",
                expectedSymbolName: "Widget");
        Assert(
            replay.Success &&
            replay.Replayed &&
            replay.Transaction.Replayed,
            replay.Transaction.Error?.Message ??
            "Semantic durable replay failed.");
    }

    private static async Task SemanticProjectScopePromotesContainingSolutionAsync()
    {
        await using var fixture =
            await TestWorkspace.CreateAsync();
        await WriteTwoProjectSemanticSolutionAsync(
            fixture);
        var anchor =
            fixture.PathInWorkspace(
                "Lib/Widget.cs");
        var read =
            await SourceEditRuntime.Engine.ReadSourceAsync(
                anchor,
                1,
                0,
                CancellationToken.None);

        var result =
            await RenameAsync(
                fixture,
                NewTransactionId(),
                "Lib/Lib.csproj",
                "Lib/Widget.cs",
                1,
                13,
                read.Revision,
                "ProjectScopedWidget",
                projectPath: "Lib/Lib.csproj",
                expectedSymbolName: "Widget");

        Assert(
            result.Success,
            result.Transaction.Error?.Message ??
            "Project-scope semantic rename did not promote to the containing solution.");
        Assert(
            result.Workspace is not null &&
            result.Workspace.InputKind == "solution" &&
            result.Workspace.InputPath.Replace(
                '\\',
                '/') == "Demo.slnx" &&
            result.Workspace.ProjectCount == 2,
            "Project-scope semantic receipt did not report the promoted full-solution scope.");
        Assert(
            (await File.ReadAllTextAsync(
                fixture.PathInWorkspace(
                    "App/Use.cs")))
            .Contains(
                "ProjectScopedWidget",
                StringComparison.Ordinal),
            "Project-scope semantic rename did not update the reverse-dependent project.");
    }

    private static async Task SemanticProjectScopeRejectsUnprovenMultiProjectAsync()
    {
        await using var fixture =
            await TestWorkspace.CreateAsync();
        await WriteTwoProjectSemanticSolutionAsync(
            fixture);
        File.Delete(
            fixture.PathInWorkspace(
                "Demo.slnx"));
        var anchor =
            fixture.PathInWorkspace(
                "Lib/Widget.cs");
        var originalApp =
            await File.ReadAllTextAsync(
                fixture.PathInWorkspace(
                    "App/Use.cs"));
        var read =
            await SourceEditRuntime.Engine.ReadSourceAsync(
                anchor,
                1,
                0,
                CancellationToken.None);

        var result =
            await RenameAsync(
                fixture,
                NewTransactionId(),
                "Lib/Lib.csproj",
                "Lib/Widget.cs",
                1,
                13,
                read.Revision,
                "ShouldNotCommit",
                projectPath: "Lib/Lib.csproj",
                expectedSymbolName: "Widget");

        AssertError(
            result.Transaction,
            SourceEditCodes.SemanticScopeIncomplete);
        Assert(
            (await File.ReadAllTextAsync(anchor))
            .Contains(
                "class Widget",
                StringComparison.Ordinal),
            "Incomplete project scope unexpectedly changed the declaration.");
        AssertEqual(
            originalApp,
            await File.ReadAllTextAsync(
                fixture.PathInWorkspace(
                    "App/Use.cs")),
            "Incomplete project scope unexpectedly changed a reverse-dependent project.");
    }

    private static async Task SemanticBaselineCompilationErrorsAsync()
    {
        await using var fixture =
            await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async(
            "App/App.csproj",
            Project(
                itemGroup:
                    "<Reference Include=\"Missing.Dependency\"><HintPath>missing\\Missing.Dependency.dll</HintPath></Reference>"));
        const string source =
            "namespace Demo;\n" +
            "public class Widget {}\n" +
            "public class UsesMissing { public Missing.Dependency.Type? Value { get; set; } }\n";
        await fixture.WriteUtf8Async(
            "App/Widget.cs",
            source);
        await fixture.WriteUtf8Async(
            "Demo.slnx",
            "<Solution>\n  <Project Path=\"App/App.csproj\" />\n</Solution>\n");
        var anchor =
            fixture.PathInWorkspace(
                "App/Widget.cs");
        var read =
            await SourceEditRuntime.Engine.ReadSourceAsync(
                anchor,
                1,
                0,
                CancellationToken.None);

        var result =
            await RenameAsync(
                fixture,
                NewTransactionId(),
                "Demo.slnx",
                "App/Widget.cs",
                1,
                13,
                read.Revision,
                "RenamedWidget",
                projectPath: "App/App.csproj",
                expectedSymbolName: "Widget");

        AssertError(
            result.Transaction,
            SourceEditCodes.SemanticCompilationInvalid);
        Assert(
            result.Diagnostics.Any(
                diagnostic =>
                    diagnostic.Source ==
                        "csharp-compiler" &&
                    diagnostic.Severity ==
                        "error"),
            "Baseline compiler errors were not surfaced in semantic diagnostics.");
        AssertEqual(
            source,
            await File.ReadAllTextAsync(anchor),
            "Baseline compiler-error rejection unexpectedly mutated source.");
    }

    private static async Task SemanticRenameCompilerConflictAsync()
    {
        await using var fixture =
            await TestWorkspace.CreateAsync();
        const string source =
            "namespace Demo;\n" +
            "public class Alpha {}\n" +
            "public class Beta {}\n";
        await WriteSingleProjectSemanticSolutionAsync(
            fixture,
            source);
        var anchor =
            fixture.PathInWorkspace(
                "App/Widget.cs");
        var read =
            await SourceEditRuntime.Engine.ReadSourceAsync(
                anchor,
                1,
                0,
                CancellationToken.None);

        var result =
            await RenameAsync(
                fixture,
                NewTransactionId(),
                "Demo.slnx",
                "App/Widget.cs",
                1,
                13,
                read.Revision,
                "Beta",
                projectPath: "App/App.csproj",
                expectedSymbolName: "Alpha");

        AssertError(
            result.Transaction,
            SourceEditCodes.SemanticConflict);
        Assert(
            result.Diagnostics.Any(
                diagnostic =>
                    diagnostic.Source ==
                        "csharp-compiler" &&
                    diagnostic.Code ==
                        "CS0101"),
            "Rename compiler conflict was not surfaced as CS0101.");
        AssertEqual(
            source,
            await File.ReadAllTextAsync(anchor),
            "Compiler-conflict rejection unexpectedly mutated source.");
    }

    private static async Task SemanticStaleAnchorAsync()
    {
        await using var fixture =
            await TestWorkspace.CreateAsync();
        await WriteSingleProjectSemanticSolutionAsync(
            fixture,
            "public class Widget {}\n");
        var anchor =
            fixture.PathInWorkspace(
                "App/Widget.cs");
        var read =
            await SourceEditRuntime.Engine.ReadSourceAsync(
                anchor,
                1,
                0,
                CancellationToken.None);
        await fixture.WriteUtf8Async(
            "App/Widget.cs",
            "public class Widget { public int X; }\n");

        var result =
            await RenameAsync(
                fixture,
                NewTransactionId(),
                "Demo.slnx",
                "App/Widget.cs",
                0,
                13,
                read.Revision,
                "RenamedWidget",
                projectPath: "App/App.csproj",
                expectedSymbolName: "Widget");
        AssertError(
            result.Transaction,
            SourceEditCodes.ExpectedRevisionMismatch);
        AssertEqual(
            "public class Widget { public int X; }\n",
            await File.ReadAllTextAsync(
                anchor),
            "Stale semantic anchor unexpectedly mutated.");
    }

    private static async Task SemanticNoSymbolAsync()
    {
        await using var fixture =
            await TestWorkspace.CreateAsync();
        const string source =
            "// no symbol here\npublic class Widget {}\n";
        await WriteSingleProjectSemanticSolutionAsync(
            fixture,
            source);
        var anchor =
            fixture.PathInWorkspace(
                "App/Widget.cs");
        var read =
            await SourceEditRuntime.Engine.ReadSourceAsync(
                anchor,
                1,
                0,
                CancellationToken.None);

        var result =
            await RenameAsync(
                fixture,
                NewTransactionId(),
                "Demo.slnx",
                "App/Widget.cs",
                0,
                3,
                read.Revision,
                "RenamedWidget",
                projectPath: "App/App.csproj");
        AssertError(
            result.Transaction,
            SourceEditCodes.SemanticSymbolNotFound);
        AssertEqual(
            source,
            await File.ReadAllTextAsync(
                anchor),
            "No-symbol semantic rejection unexpectedly mutated.");
    }

    private static async Task SemanticLinkedContextAmbiguityAsync()
    {
        await using var fixture =
            await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async(
            "Shared.cs",
            "#if A\nnamespace One;\n#else\nnamespace Two;\n#endif\npublic class Widget {}\n");
        await fixture.WriteUtf8Async(
            "P1/P1.csproj",
            Project(
                "<DefineConstants>A</DefineConstants><EnableDefaultCompileItems>false</EnableDefaultCompileItems>",
                "<Compile Include=\"..\\Shared.cs\" Link=\"Shared.cs\" />"));
        await fixture.WriteUtf8Async(
            "P2/P2.csproj",
            Project(
                "<DefineConstants>B</DefineConstants><EnableDefaultCompileItems>false</EnableDefaultCompileItems>",
                "<Compile Include=\"..\\Shared.cs\" Link=\"Shared.cs\" />"));
        await fixture.WriteUtf8Async(
            "Demo.slnx",
            "<Solution>\n  <Project Path=\"P1/P1.csproj\" />\n  <Project Path=\"P2/P2.csproj\" />\n</Solution>\n");
        var anchor =
            fixture.PathInWorkspace(
                "Shared.cs");
        var read =
            await SourceEditRuntime.Engine.ReadSourceAsync(
                anchor,
                1,
                0,
                CancellationToken.None);

        var result =
            await RenameAsync(
                fixture,
                NewTransactionId(),
                "Demo.slnx",
                "Shared.cs",
                5,
                13,
                read.Revision,
                "RenamedWidget",
                projectPath: null,
                expectedSymbolName: "Widget");
        AssertError(
            result.Transaction,
            SourceEditCodes.SemanticSymbolAmbiguous);
        Assert(
            (await File.ReadAllTextAsync(
                anchor))
            .Contains(
                "class Widget",
                StringComparison.Ordinal),
            "Ambiguous semantic identity unexpectedly mutated.");
    }

    private static async Task SemanticWorkspaceLoadDiagnosticsAsync()
    {
        await using var fixture =
            await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async(
            "Broken/Broken.csproj",
            "<Project Sdk=\"Definitely.Missing.Sdk/999.0.0\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>\n");
        await fixture.WriteUtf8Async(
            "Broken/Widget.cs",
            "public class Widget {}\n");
        var anchor =
            fixture.PathInWorkspace(
                "Broken/Widget.cs");
        var read =
            await SourceEditRuntime.Engine.ReadSourceAsync(
                anchor,
                1,
                0,
                CancellationToken.None);

        var result =
            await RenameAsync(
                fixture,
                NewTransactionId(),
                "Broken/Broken.csproj",
                "Broken/Widget.cs",
                0,
                13,
                read.Revision,
                "RenamedWidget",
                projectPath: "Broken/Broken.csproj",
                expectedSymbolName: "Widget");
        AssertError(
            result.Transaction,
            SourceEditCodes.SemanticWorkspaceLoadFailed);
        AssertEqual(
            "public class Widget {}\n",
            await File.ReadAllTextAsync(
                anchor),
            "Workspace-load semantic rejection unexpectedly mutated.");
    }

    private static async Task SemanticCancellationAsync()
    {
        await using var fixture =
            await TestWorkspace.CreateAsync();
        const string source =
            "public class Widget {}\n";
        await WriteSingleProjectSemanticSolutionAsync(
            fixture,
            source);
        var anchor =
            fixture.PathInWorkspace(
                "App/Widget.cs");
        var read =
            await SourceEditRuntime.Engine.ReadSourceAsync(
                anchor,
                1,
                0,
                CancellationToken.None);
        using var cancellation =
            new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            _ =
                await RenameAsync(
                    fixture,
                    NewTransactionId(),
                    "Demo.slnx",
                    "App/Widget.cs",
                    0,
                    13,
                    read.Revision,
                    "RenamedWidget",
                    projectPath: "App/App.csproj",
                    expectedSymbolName: "Widget",
                    cancellationToken: cancellation.Token);
            throw new InvalidOperationException(
                "Pre-cancelled semantic operation unexpectedly completed.");
        }
        catch (OperationCanceledException)
        {
        }

        AssertEqual(
            source,
            await File.ReadAllTextAsync(
                anchor),
            "Cancelled semantic operation unexpectedly mutated.");
    }

    private static async Task<SemanticEditTransactionResult> RenameAsync(
        TestWorkspace fixture,
        string transactionId,
        string solutionOrProjectPath,
        string documentPath,
        int line,
        int character,
        string revision,
        string newName,
        string? projectPath,
        string? expectedSymbolName = null,
        CancellationToken cancellationToken = default) =>
        await RoslynSemanticEditEngine.ApplyRenameAsync(
            fixture.Root,
            transactionId,
            solutionOrProjectPath,
            documentPath,
            line,
            character,
            revision,
            newName,
            projectPath,
            expectedSymbolName,
            renameOverloads: false,
            renameInStrings: false,
            renameInComments: false,
            maxProjects: 32,
            maxDocuments: 256,
            maxChangedDocuments: 64,
            maxTotalChangedCharacters: 1024 * 1024,
            maxDiagnostics: 100,
            timeoutSeconds: 120,
            validateSyntax: true,
            cancellationToken);

    private static async Task WriteTwoProjectSemanticSolutionAsync(
        TestWorkspace fixture)
    {
        await fixture.WriteUtf8Async(
            "Lib/Lib.csproj",
            Project());
        await fixture.WriteUtf8Async(
            "App/App.csproj",
            Project(
                itemGroup:
                    "<ProjectReference Include=\"..\\Lib\\Lib.csproj\" />"));
        var libPath =
            fixture.PathInWorkspace(
                "Lib/Widget.cs");
        Directory.CreateDirectory(
            Path.GetDirectoryName(
                libPath)!);
        await File.WriteAllTextAsync(
            libPath,
            "namespace Demo;\r\npublic class Widget { public int X { get; set; } }\r\n",
            new UnicodeEncoding(
                bigEndian: false,
                byteOrderMark: true));
        await fixture.WriteUtf8Async(
            "App/Use.cs",
            "using Demo;\r\nclass Use {  Widget   value = new Widget(); }\r\n");
        await fixture.WriteUtf8Async(
            "Demo.slnx",
            "<Solution>\n  <Project Path=\"Lib/Lib.csproj\" />\n  <Project Path=\"App/App.csproj\" />\n</Solution>\n");
    }

    private static async Task WriteSingleProjectSemanticSolutionAsync(
        TestWorkspace fixture,
        string source)
    {
        await fixture.WriteUtf8Async(
            "App/App.csproj",
            Project());
        await fixture.WriteUtf8Async(
            "App/Widget.cs",
            source);
        await fixture.WriteUtf8Async(
            "Demo.slnx",
            "<Solution>\n  <Project Path=\"App/App.csproj\" />\n</Solution>\n");
    }

    private static string Project(
        string? propertyGroup = null,
        string? itemGroup = null) =>
        "<Project Sdk=\"Microsoft.NET.Sdk\">" +
        "<PropertyGroup><TargetFramework>net10.0</TargetFramework>" +
        (propertyGroup ?? string.Empty) +
        "</PropertyGroup>" +
        (itemGroup is null
            ? string.Empty
            : "<ItemGroup>" + itemGroup + "</ItemGroup>") +
        "</Project>\n";
}
