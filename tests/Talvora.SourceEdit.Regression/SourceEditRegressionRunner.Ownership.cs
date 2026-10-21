using System.Security.Principal;
using Talvora.SourceEditing;

internal static partial class SourceEditRegressionRunner
{
    public static async Task RunOwnershipContractsAsync()
    {
        await UpdateRetainsOwnerAsync();
        await AddedFileAndDirectoryUseRootOwnerAsync();
        if (WindowsIdentity.GetCurrent().IsSystem)
        {
            await SystemOwnedFileRetainsAdministratorsOwnerAsync();
        }
        else
        {
            Console.WriteLine("SKIP owner-admin-system-only: requires LocalSystem.");
        }
    }

    private static async Task UpdateRetainsOwnerAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async("owned.txt", "before\n");
        var path = fixture.PathInWorkspace("owned.txt");
        var originalOwner = FileOwnership.TryGetOwner(path)
            ?? throw new InvalidOperationException("Could not read initial file owner.");
        var engine = fixture.CreateEngine();
        var revision = await RevisionAsync(engine, path);
        var result = await engine.ApplyPatchAsync(
            fixture.Root, NewTransactionId(),
            PatchUpdate("owned.txt", revision, "-before", "+after"),
            true, CancellationToken.None);
        Assert(result.Success, result.Error?.Message ?? "Owned file update failed.");
        AssertEqual(
            originalOwner.Value,
            FileOwnership.TryGetOwner(path)?.Value,
            "Updating a file must retain its original owner.");
        AssertEqual(
            0,
            result.Warnings.Count(w => w.Code == "OWNER_NOT_PRESERVED"),
            "A successful owner-preserving update must not report an owner warning.");
    }

    private static async Task AddedFileAndDirectoryUseRootOwnerAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        var rootOwner = FileOwnership.TryGetOwner(fixture.Root)
            ?? throw new InvalidOperationException("Could not read workspace root owner.");
        if (WindowsIdentity.GetCurrent().IsSystem)
        {
            var administrators = new SecurityIdentifier(
                WellKnownSidType.BuiltinAdministratorsSid, null);
            Assert(
                FileOwnership.TrySetOwner(fixture.Root, administrators),
                "Could not prepare an Administrators-owned test workspace.");
            rootOwner = administrators;
        }

        var result = await fixture.CreateEngine().ApplyEditsAsync(
            fixture.Root,
            NewTransactionId(),
            [new SourceEditChangeInput
            {
                Operation = "add",
                Path = "nested/new.txt",
                Content = "created\n",
            }],
            true,
            CancellationToken.None);
        Assert(result.Success, result.Error?.Message ?? "Owner test add failed.");
        AssertEqual(
            rootOwner.Value,
            FileOwnership.TryGetOwner(fixture.PathInWorkspace("nested/new.txt"))?.Value,
            "Added files must use the workspace root owner.");
        AssertEqual(
            rootOwner.Value,
            FileOwnership.TryGetOwner(fixture.PathInWorkspace("nested"))?.Value,
            "Created directories must use the workspace root owner.");
        AssertEqual(
            0,
            result.Warnings.Count(w => w.Code == "OWNER_NOT_PRESERVED"),
            "Correct owner assignments must not report owner warnings.");
    }

    private static async Task SystemOwnedFileRetainsAdministratorsOwnerAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async("admin-owned.txt", "before\n");
        var path = fixture.PathInWorkspace("admin-owned.txt");
        var administrators = new SecurityIdentifier(
            WellKnownSidType.BuiltinAdministratorsSid, null);
        Assert(
            FileOwnership.TrySetOwner(path, administrators),
            "Could not prepare an Administrators-owned file.");
        var engine = fixture.CreateEngine();
        var revision = await RevisionAsync(engine, path);
        var result = await engine.ApplyPatchAsync(
            fixture.Root, NewTransactionId(),
            PatchUpdate("admin-owned.txt", revision, "-before", "+after"),
            true, CancellationToken.None);
        Assert(result.Success, result.Error?.Message ?? "Administrator-owned update failed.");
        AssertEqual(
            administrators.Value,
            FileOwnership.TryGetOwner(path)?.Value,
            "LocalSystem updates must retain the Administrators owner.");
        AssertEqual(
            0,
            result.Warnings.Count(w => w.Code == "OWNER_NOT_PRESERVED"),
            "The privileged owner preservation path must be warning-free.");
    }
}
