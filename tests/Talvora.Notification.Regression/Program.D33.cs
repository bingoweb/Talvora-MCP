using Talvora;
using Talvora.Tray;

internal static partial class Program
{
    /// <summary>D33: frames get enough time on a busy machine; the tray process is verified once.</summary>
    private static void AssertD33DeliveryBudgets()
    {
        if (DesktopProgressPipeListener.PipeReadTimeout != TimeSpan.FromSeconds(10) ||
            TalvoraDesktopProgressNotifier.PipeWriteTimeout != TimeSpan.FromSeconds(8))
            throw new InvalidOperationException("D33: pipe budgets must be 10 s (tray read) and 8 s (service write).");
        var started = new DateTime(2026, 11, 7, 9, 0, 0, DateTimeKind.Utc);
        TrayPipeVerificationCache.Store(4242, started, trusted: true);
        if (!TrayPipeVerificationCache.TryGet(4242, started, out var trusted) || !trusted)
            throw new InvalidOperationException("D33: the same tray process must reuse its verdict.");
        if (TrayPipeVerificationCache.TryGet(4242, started.AddSeconds(1), out _))
            throw new InvalidOperationException("D33: a reused process id with another start time must be verified again.");
        Console.WriteLine("D33_DELIVERY_BUDGETS_GREEN");
    }
}
