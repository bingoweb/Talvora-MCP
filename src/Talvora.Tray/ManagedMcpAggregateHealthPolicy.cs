using Talvora.Shared;

namespace Talvora.Tray;

internal readonly record struct AggregateMcpState(
    ControlCenterHealthState? Health,
    bool Ignored);

internal readonly record struct AggregateMcpResult(
    TalvoraConnectionState State,
    int AttentionCount);

internal static class ManagedMcpAggregateHealthPolicy
{
    // An explicitly stopped MCP or an inactive on-demand integration is not a failure.
    // The Talvora core remains mandatory even if someone requests it to stop.
    internal static bool IsIgnored(
        ManagedMcpRegistration registration,
        ControlCenterHealthState? health,
        bool manuallyStopped) =>
        !string.Equals(
            registration.Id,
            "talvora",
            StringComparison.OrdinalIgnoreCase) &&
        (manuallyStopped ||
         (!registration.AutoStart &&
          health is null or ControlCenterHealthState.Offline));

    internal static AggregateMcpResult Evaluate(
        TalvoraConnectionState talvoraState,
        GiteaConnectionState giteaState,
        bool ignoreGitea,
        IReadOnlyList<AggregateMcpState> genericStates)
    {
        var genericOffline = genericStates.Any(state =>
            !state.Ignored &&
            state.Health == ControlCenterHealthState.Offline);
        var genericNotReady = genericStates.Count(state =>
            !state.Ignored &&
            state.Health != ControlCenterHealthState.Ready);
        var giteaReady = ignoreGitea ||
            giteaState == GiteaConnectionState.Running;
        var giteaOffline = !ignoreGitea &&
            giteaState == GiteaConnectionState.Offline;
        var attentionCount =
            (talvoraState == TalvoraConnectionState.Ready ? 0 : 1) +
            (giteaReady ? 0 : 1) +
            genericNotReady;

        var aggregate = talvoraState == TalvoraConnectionState.Offline ||
                        giteaOffline ||
                        genericOffline
            ? TalvoraConnectionState.Offline
            : talvoraState == TalvoraConnectionState.Ready &&
              giteaReady &&
              genericNotReady == 0
                ? TalvoraConnectionState.Ready
                : TalvoraConnectionState.LocalOnly;

        return new AggregateMcpResult(aggregate, attentionCount);
    }

    internal static void AssertContract()
    {
        var required = new ManagedMcpRegistration
        {
            Id = "required",
            DisplayName = "Required",
            Description = "Required component",
            Endpoint = "http://127.0.0.1:65530/mcp",
        };
        var optional = required with
        {
            Id = "optional",
            AutoStart = false,
        };
        var core = required with { Id = "talvora" };

        if (!IsIgnored(required, ControlCenterHealthState.Offline, true) ||
            !IsIgnored(optional, ControlCenterHealthState.Offline, false) ||
            !IsIgnored(optional, null, false) ||
            IsIgnored(required, ControlCenterHealthState.Offline, false) ||
            IsIgnored(optional, ControlCenterHealthState.Ready, false) ||
            IsIgnored(optional, ControlCenterHealthState.Attention, false) ||
            IsIgnored(core, ControlCenterHealthState.Offline, true))
        {
            throw new InvalidOperationException(
                "Optional and manually stopped MCP classification is incorrect.");
        }

        var ignored = Evaluate(
            TalvoraConnectionState.Ready,
            GiteaConnectionState.Offline,
            ignoreGitea: true,
            [
                new AggregateMcpState(ControlCenterHealthState.Offline, true),
                new AggregateMcpState(null, true),
                new AggregateMcpState(ControlCenterHealthState.Ready, false),
            ]);
        if (ignored.State != TalvoraConnectionState.Ready ||
            ignored.AttentionCount != 0)
        {
            throw new InvalidOperationException(
                "Intentionally stopped MCPs changed the tray from green.");
        }

        var realGiteaFailure = Evaluate(
            TalvoraConnectionState.Ready,
            GiteaConnectionState.Offline,
            ignoreGitea: false,
            []);
        var realGenericFailure = Evaluate(
            TalvoraConnectionState.Ready,
            GiteaConnectionState.Running,
            ignoreGitea: false,
            [new AggregateMcpState(ControlCenterHealthState.Offline, false)]);
        var pendingRequired = Evaluate(
            TalvoraConnectionState.Ready,
            GiteaConnectionState.Running,
            ignoreGitea: false,
            [new AggregateMcpState(null, false)]);
        var attention = Evaluate(
            TalvoraConnectionState.Ready,
            GiteaConnectionState.Running,
            ignoreGitea: false,
            [new AggregateMcpState(ControlCenterHealthState.Attention, false)]);
        var coreFailure = Evaluate(
            TalvoraConnectionState.Offline,
            GiteaConnectionState.Offline,
            ignoreGitea: true,
            [new AggregateMcpState(ControlCenterHealthState.Offline, true)]);

        if (realGiteaFailure.State != TalvoraConnectionState.Offline ||
            realGenericFailure.State != TalvoraConnectionState.Offline ||
            pendingRequired.State != TalvoraConnectionState.LocalOnly ||
            pendingRequired.AttentionCount != 1 ||
            attention.State != TalvoraConnectionState.LocalOnly ||
            attention.AttentionCount != 1 ||
            coreFailure.State != TalvoraConnectionState.Offline)
        {
            throw new InvalidOperationException(
                "A real MCP failure was incorrectly hidden from the tray.");
        }
    }
}
