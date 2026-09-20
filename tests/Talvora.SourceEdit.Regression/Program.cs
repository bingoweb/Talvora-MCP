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

await SourceEditRegressionRunner.RunAllAsync();
Console.WriteLine("TALVORA SOURCE EDIT REGRESSION GREEN");
