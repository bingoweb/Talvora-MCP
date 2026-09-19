namespace Talvora.Tray;

internal sealed class ManagedMcpOperationInProgressException
    : InvalidOperationException
{
    public ManagedMcpOperationInProgressException(string mcpId)
        : base($"{mcpId} için başka bir yaşam döngüsü işlemi halen sürüyor.")
    {
        McpId = mcpId;
    }

    public string McpId { get; }
}

internal static class ManagedMcpOperationCoordinator
{
    public static IDisposable? TryAcquire(string mcpId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mcpId);

        var safeId = new string(
            mcpId
                .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'
                    ? ch
                    : '_')
                .ToArray());

        var semaphore = new Semaphore(
            initialCount: 1,
            maximumCount: 1,
            name: @"Local\Talvora.ManagedMcpOperation." + safeId);

        try
        {
            if (!semaphore.WaitOne(0))
            {
                semaphore.Dispose();
                return null;
            }

            return new Lease(semaphore);
        }
        catch
        {
            semaphore.Dispose();
            throw;
        }
    }

    internal static void AssertContract()
    {
        var id = "self-test-" + Guid.NewGuid().ToString("N");
        using var first = TryAcquire(id)
            ?? throw new InvalidOperationException(
                "Managed MCP operation guard first acquisition failed.");

        using var blocked = TryAcquire(id);
        if (blocked is not null)
        {
            throw new InvalidOperationException(
                "Managed MCP operation guard allowed concurrent acquisition.");
        }

        first.Dispose();

        using var afterRelease = TryAcquire(id);
        if (afterRelease is null)
        {
            throw new InvalidOperationException(
                "Managed MCP operation guard did not release correctly.");
        }
    }

    private sealed class Lease : IDisposable
    {
        private Semaphore? _semaphore;

        public Lease(Semaphore semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            var semaphore = Interlocked.Exchange(
                ref _semaphore,
                null);
            if (semaphore is null)
            {
                return;
            }

            try
            {
                semaphore.Release();
            }
            finally
            {
                semaphore.Dispose();
            }
        }
    }
}
