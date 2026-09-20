using Talvora.SourceEditing;
using Talvora.Tools;

internal static partial class SourceEditRegressionRunner
{
    public static async Task RunSourceAuditAsync()
    {
        var failures = new List<string>();
        foreach (var (name, run) in new (string, Func<Task>)[]
        {
            ("csharp-invalid-patch-zero-mutation", CSharpInvalidPatchAsync),
            ("csharp-valid-syntax-and-opt-out", CSharpValidSyntaxAsync),
            ("structural-yaml-control-isolation", StructuralYamlControlIsolationAsync),
        })
        {
            try { await run(); Console.WriteLine($"PASS {name}"); }
            catch (Exception ex) { failures.Add($"{name}: {ex}"); Console.WriteLine($"FAIL {name}: {ex.Message}"); }
        }
        if (failures.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }

    private static async Task CSharpInvalidPatchAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        const string original = "public class Demo { public int X => 1; }";
        await fixture.WriteUtf8Async("Demo.cs", original + "\n");
        await fixture.WriteUtf8Async("note.txt", "before\n");
        var engine = fixture.CreateEngine();
        var path = fixture.PathInWorkspace("Demo.cs");
        var revision = await RevisionAsync(engine, path);
        var noteRevision = await RevisionAsync(engine, fixture.PathInWorkspace("note.txt"));
        var patch = string.Join("\n",
            "*** Begin Talvora Patch",
            "*** Update File: note.txt", $"*** Revision: {noteRevision}", "@@", "-before", "+after",
            "*** Update File: Demo.cs", $"*** Revision: {revision}", "@@",
            "-" + original, "+public class Demo { public int X => ; }",
            "*** End Talvora Patch");
        var result = await engine.ApplyPatchAsync(fixture.Root, NewTransactionId(), patch, true, CancellationToken.None);
        AssertError(result, SourceEditCodes.ValidationFailed);
        AssertEqual(original + "\n", await File.ReadAllTextAsync(path), "Invalid C# was published.");
        AssertEqual("before\n", await File.ReadAllTextAsync(fixture.PathInWorkspace("note.txt")), "Preflight partially wrote another file.");
    }

    private static async Task CSharpValidSyntaxAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        var engine = fixture.CreateEngine();
        var result = await engine.ApplyEditsAsync(fixture.Root, NewTransactionId(),
            [new SourceEditChangeInput { Operation = "add", Path = "Valid.cs", Content = "public class Demo { public MissingType Value { get; set; } }\n" }],
            true, CancellationToken.None);
        Assert(result.Success, result.Error?.Message ?? "Valid syntax rejected.");
        Assert(result.Validation.Any(v => v.Validator == "Roslyn.CSharp" && v.Status == "passed"), "C# validation receipt absent.");
        result = await engine.ApplyEditsAsync(fixture.Root, NewTransactionId(),
            [new SourceEditChangeInput { Operation = "add", Path = "Draft.cs", Content = "public class Unfinished {\n" }],
            false, CancellationToken.None);
        Assert(result.Success && result.Validation.Any(v => v.Status == "skipped"), "Explicit syntax opt-out no longer works.");
        result = await engine.ApplyEditsAsync(fixture.Root, NewTransactionId(),
            [new SourceEditChangeInput { Operation = "add", Path = "Script.csx", Content = "await System.Threading.Tasks.Task.Delay(1);\n" }],
            true, CancellationToken.None);
        Assert(result.Success && result.Validation.Any(v => v.Validator == "Roslyn.CSharp"), "C# script syntax was not checked.");
    }

    private static async Task StructuralYamlControlIsolationAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        const string rule = "id: rename-foo\nlanguage: Yaml\nrule:\n  pattern: foo\nfix: bar\n";
        await fixture.WriteUtf8Async("rule.yml", rule);
        await fixture.WriteUtf8Async("data.yml", "value: foo\n");
        await fixture.WriteUtf8Async(".talvora-structural-rule.yml", "value: foo\n");
        var transaction = NewTransactionId();
        var result = await StructuralEditTools.StructuralEdit(fixture.Root, transaction,
            ruleFile: "rule.yml", paths: ["."], includeHidden: true, timeoutSeconds: 20);
        Assert(result.Success, result.Error?.Message ?? "YAML structural transformation failed.");
        AssertEqual("value: bar\n", await File.ReadAllTextAsync(fixture.PathInWorkspace("data.yml")), "YAML data was not transformed.");
        AssertEqual("value: bar\n", await File.ReadAllTextAsync(fixture.PathInWorkspace(".talvora-structural-rule.yml")), "User file collided with helper rule.");
        AssertEqual(rule, await File.ReadAllTextAsync(fixture.PathInWorkspace("rule.yml")), "Rule input was edited as source.");
        AssertEqual(2, result.Files.Count, "Control files leaked into the transformation.");
    }
}
