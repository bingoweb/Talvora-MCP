using Talvora.Tray;

internal static partial class Program
{
    private static void AssertD115GiteaRepairEscalationRule()
    {
        for (var bits = 0; bits < 32; bits++)
        {
            var probe = new GiteaChainProbe(
                Backend: (bits & 1) != 0,
                Proxy: (bits & 2) != 0,
                McpServer: (bits & 4) != 0,
                Protocol: (bits & 8) != 0,
                Tunnel: (bits & 16) != 0);
            var expected = bits switch
            {
                31 => GiteaRepairEscalation.NotNeeded,
                15 => GiteaRepairEscalation.TunnelOnlyFailure,
                _ => GiteaRepairEscalation.FullChainRestart,
            };
            var actual = GiteaTrayClient.DecideRepairEscalation(probe);
            if (actual != expected)
                throw new InvalidOperationException(
                    $"D115: {probe} expected {expected}, got {actual}.");
        }

        Console.WriteLine("D115_GITEA_ESCALATION_GREEN");
    }
}
