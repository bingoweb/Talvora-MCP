// S3 (P-29): the signed-in gate cannot write ProgramData\Talvora. Engine state and the ast-grep cache live in
// a user-owned folder that persists between runs (so the pinned ast-grep download is cached, as before).
var regressionState = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Talvora", "RegressionState");
// T1: the semantic scenarios run in a parallel process of their own, so they keep their own engine state.
var semanticOnly = args.Contains("--semantic-only", StringComparer.OrdinalIgnoreCase);
Talvora.SourceEditing.SourceEditRuntime.UseStateRootForTests(
    Path.Combine(regressionState, semanticOnly ? "SourceEdit-Semantic" : "SourceEdit"));
Talvora.SourceEditing.AstGrepToolchainManager.CacheRoot = Path.Combine(regressionState, "Toolchains");

if (args.Contains("--toolchain-q2a-only", StringComparer.OrdinalIgnoreCase))
{
    await SourceEditRegressionRunner.RunToolchainConsolidationAsync();
    Console.WriteLine("TALVORA TOOLCHAIN Q2A GREEN");
    return;
}
if (args.Contains("--receipt-counts-only", StringComparer.OrdinalIgnoreCase))
{
    await SourceEditRegressionRunner.RunReceiptCountsAsync();
    Console.WriteLine("TALVORA RECEIPT COUNTS GREEN");
    return;
}
if (args.Contains("--knowledge-s6-local", StringComparer.OrdinalIgnoreCase))
{
    await SourceEditRegressionRunner.RunKnowledgeS6LocalAsync();
    Console.WriteLine("TALVORA S6 KNOWLEDGE LOCAL GREEN");
    return;
}
if (args.Contains("--ast-grep-pin-only", StringComparer.OrdinalIgnoreCase))
{
    await SourceEditRegressionRunner.RunPinnedAstGrepCacheAsync();
    Console.WriteLine("TALVORA AST GREP PIN REGRESSION GREEN");
    return;
}

if (args.Contains("--work-contract-only", StringComparer.OrdinalIgnoreCase))
{
    await SourceEditRegressionRunner.RunWorkContractAsync();
    Console.WriteLine("TALVORA WORK CONTRACT REGRESSION GREEN");
    return;
}

if (args.Contains("--quality-audit-only", StringComparer.OrdinalIgnoreCase))
{
    await SourceEditRegressionRunner.RunQualityAuditAsync();
    Console.WriteLine("TALVORA QUALITY AUDIT REGRESSION GREEN");
    return;
}

if (args.Contains(
        "--semantic-solution-only",
        StringComparer.OrdinalIgnoreCase))
{
    await SourceEditRegressionRunner.RunSemanticSolutionRenameAsync();
    Console.WriteLine(
        "TALVORA SEMANTIC SOLUTION REGRESSION GREEN");
    return;
}

if (args.Contains(
        "--semantic-toolchain-isolation-only",
        StringComparer.OrdinalIgnoreCase))
{
    await SourceEditRegressionRunner.RunSemanticToolchainIsolationAsync();
    Console.WriteLine(
        "TALVORA SEMANTIC TOOLCHAIN ISOLATION REGRESSION GREEN");
    return;
}

if (args.Contains(
        "--semantic-graph-stale-only",
        StringComparer.OrdinalIgnoreCase))
{
    await SourceEditRegressionRunner.RunSemanticGraphStaleAsync();
    Console.WriteLine(
        "TALVORA SEMANTIC GRAPH STALE REGRESSION GREEN");
    return;
}

if (args.Contains(
        "--semantic-resource-bounds-only",
        StringComparer.OrdinalIgnoreCase))
{
    await SourceEditRegressionRunner.RunSemanticResourceBoundsAsync();
    Console.WriteLine(
        "TALVORA SEMANTIC RESOURCE BOUNDS REGRESSION GREEN");
    return;
}

if (args.Contains(
        "--response-bounds-only",
        StringComparer.OrdinalIgnoreCase))
{
    await SourceEditRegressionRunner.RunResponseBoundsAsync();
    Console.WriteLine(
        "TALVORA RESPONSE BOUNDS REGRESSION GREEN");
    return;
}

if (args.Contains(
        "--runtime-bounds-only",
        StringComparer.OrdinalIgnoreCase))
{
    await SourceEditRegressionRunner.RunRuntimeBoundsAsync();
    Console.WriteLine(
        "TALVORA RUNTIME BOUNDS REGRESSION GREEN");
    return;
}

if (args.Contains("--source-cache-only", StringComparer.OrdinalIgnoreCase))
{
    await SourceEditRegressionRunner.RunCacheAuditAsync();
    Console.WriteLine("TALVORA CACHE AUDIT REGRESSION GREEN");
    return;
}

if (args.Contains("--source-audit-only", StringComparer.OrdinalIgnoreCase))
{
    await SourceEditRegressionRunner.RunSourceAuditAsync();
    Console.WriteLine("TALVORA SOURCE AUDIT REGRESSION GREEN");
    return;
}

if (args.Contains(
        "--job-storage-only",
        StringComparer.OrdinalIgnoreCase))
{
    await Talvora.Tools.JobTools.AssertStorageContractAsync(
        CancellationToken.None);
    Console.WriteLine(
        "TALVORA JOB STORAGE REGRESSION GREEN");
    return;
}

if (args.Contains(
        "--job-stop-race-only",
        StringComparer.OrdinalIgnoreCase))
{
    await Talvora.Tools.JobTools.AssertStopMetadataPublicationContractAsync(
        CancellationToken.None);
    Console.WriteLine(
        "TALVORA JOB STOP METADATA REGRESSION GREEN");
    return;
}

if (args.Contains("--git-argument-classification-only", StringComparer.OrdinalIgnoreCase))
{
    await SourceEditRegressionRunner.RunGitArgumentClassificationAsync();
    Console.WriteLine("TALVORA GIT ARGUMENT CLASSIFICATION GREEN");
    return;
}
if (args.Contains("--directory-guard-contract-only", StringComparer.OrdinalIgnoreCase))
{
    await SourceEditRegressionRunner.RunDirectoryGuardContractsAsync();
    Console.WriteLine("TALVORA DIRECTORY GUARD CONTRACT REGRESSION GREEN");
    return;
}
if (args.Contains("--directory-identity-sharing-only", StringComparer.OrdinalIgnoreCase))
{
    await SourceEditRegressionRunner.DirectoryIdentitySharingWithExternalProcessAsync();
    Console.WriteLine("TALVORA DIRECTORY IDENTITY SHARING REGRESSION GREEN");
    return;
}
if (args.Contains("--ownership-only", StringComparer.OrdinalIgnoreCase))
{
    await SourceEditRegressionRunner.RunOwnershipContractsAsync();
    Console.WriteLine("TALVORA FILE OWNERSHIP REGRESSION GREEN");
    return;
}
if (semanticOnly)
{
    await SourceEditRegressionRunner.RunAllAsync(SourceEditRegressionRunner.IsSemanticScenario);
    Console.WriteLine("TALVORA SOURCE EDIT SEMANTIC REGRESSION GREEN");
    return;
}
if (args.Contains("--without-semantic", StringComparer.OrdinalIgnoreCase))
{
    await SourceEditRegressionRunner.RunAllAsync(name => !SourceEditRegressionRunner.IsSemanticScenario(name));
    Console.WriteLine("TALVORA SOURCE EDIT CORE REGRESSION GREEN");
    return;
}
await SourceEditRegressionRunner.RunAllAsync();
Console.WriteLine("TALVORA SOURCE EDIT REGRESSION GREEN");
