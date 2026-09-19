namespace Talvora.Tray;

internal sealed record ManagedMcpRecoveryFailure(
    int ConsecutiveFailures,
    TimeSpan RetryDelay,
    DateTimeOffset NextAttemptAtUtc,
    bool IsSerious,
    bool BecameSerious);

internal sealed class ManagedMcpRecoveryState
{
    private DateTimeOffset? _firstFailureAtUtc;

    public ManagedMcpRecoveryState(string mcpId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mcpId);
        McpId = mcpId;
    }

    public string McpId { get; }

    public int ConsecutiveFailures { get; private set; }

    public DateTimeOffset NextAttemptAtUtc { get; private set; } =
        DateTimeOffset.MinValue;

    public bool SeriousIncidentNotified { get; private set; }

    public bool CanAttempt(DateTimeOffset nowUtc) =>
        nowUtc >= NextAttemptAtUtc;

    public ManagedMcpRecoveryFailure RegisterFailure(
        DateTimeOffset nowUtc)
    {
        _firstFailureAtUtc ??= nowUtc;
        ConsecutiveFailures++;

        var delay = GetBackoffDelay(ConsecutiveFailures);
        NextAttemptAtUtc = nowUtc.Add(delay);

        var elapsed = nowUtc - _firstFailureAtUtc.Value;
        var serious =
            ConsecutiveFailures >= 4 ||
            elapsed >= TimeSpan.FromMinutes(2);

        var becameSerious = serious && !SeriousIncidentNotified;
        return new ManagedMcpRecoveryFailure(
            ConsecutiveFailures,
            delay,
            NextAttemptAtUtc,
            serious,
            becameSerious);
    }

    public bool MarkSeriousIncidentNotified()
    {
        if (SeriousIncidentNotified)
        {
            return false;
        }

        SeriousIncidentNotified = true;
        return true;
    }

    public bool ResetHealthy()
    {
        var hadSeriousIncident = SeriousIncidentNotified;

        ConsecutiveFailures = 0;
        _firstFailureAtUtc = null;
        NextAttemptAtUtc = DateTimeOffset.MinValue;
        SeriousIncidentNotified = false;

        return hadSeriousIncident;
    }

    public void ResetForManualAction()
    {
        ConsecutiveFailures = 0;
        _firstFailureAtUtc = null;
        NextAttemptAtUtc = DateTimeOffset.MinValue;
        SeriousIncidentNotified = false;
    }

    internal static void AssertPolicyContract()
    {
        var state = new ManagedMcpRecoveryState("policy-self-test");
        var now = new DateTimeOffset(
            2026,
            9,
            19,
            12,
            0,
            0,
            TimeSpan.Zero);

        var first = state.RegisterFailure(now);
        var second = state.RegisterFailure(now.AddSeconds(5));
        var third = state.RegisterFailure(now.AddSeconds(15));
        var fourth = state.RegisterFailure(now.AddSeconds(35));

        if (first.RetryDelay != TimeSpan.FromSeconds(5) ||
            second.RetryDelay != TimeSpan.FromSeconds(10) ||
            third.RetryDelay != TimeSpan.FromSeconds(20) ||
            fourth.RetryDelay != TimeSpan.FromSeconds(40) ||
            !fourth.IsSerious ||
            !fourth.BecameSerious)
        {
            throw new InvalidOperationException(
                "Managed MCP recovery backoff/severity contract failed.");
        }

        if (!state.MarkSeriousIncidentNotified() ||
            !state.ResetHealthy())
        {
            throw new InvalidOperationException(
                "Managed MCP serious-incident notification contract failed.");
        }

        if (state.ConsecutiveFailures != 0 ||
            state.NextAttemptAtUtc != DateTimeOffset.MinValue)
        {
            throw new InvalidOperationException(
                "Managed MCP recovery reset contract failed.");
        }
    }

    private static TimeSpan GetBackoffDelay(int failureCount)
    {
        var seconds = failureCount switch
        {
            <= 1 => 5,
            2 => 10,
            3 => 20,
            4 => 40,
            5 => 60,
            6 => 120,
            _ => 300,
        };

        return TimeSpan.FromSeconds(seconds);
    }
}