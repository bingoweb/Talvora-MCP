using Talvora.Tray;

internal static partial class Program
{
    /// <summary>D35: a failed GitHub release lookup waits 30 minutes instead of retrying every round.</summary>
    private static async Task AssertD35LatestBackoffAsync()
    {
        var handler = new D35FailingHandler();
        var original = ManagedMcpTunnelProvisioningService.TunnelClientReleaseHttp;
        ManagedMcpTunnelProvisioningService.TunnelClientReleaseHttp = new HttpClient(handler);
        try
        {
            var first = await ManagedMcpTunnelProvisioningService.GetLatestTunnelClientTagAsync(CancellationToken.None);
            var second = await ManagedMcpTunnelProvisioningService.GetLatestTunnelClientTagAsync(CancellationToken.None);
            if (first is not null || second is not null || handler.Requests != 1)
                throw new InvalidOperationException($"D35: expected one request and no tag, got {handler.Requests} requests.");
        }
        finally
        {
            ManagedMcpTunnelProvisioningService.TunnelClientReleaseHttp = original;
        }
        Console.WriteLine("D35_LATEST_BACKOFF_GREEN");
    }

    private sealed class D35FailingHandler : HttpMessageHandler
    {
        public int Requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            throw new HttpRequestException("D35 offline");
        }
    }
}
