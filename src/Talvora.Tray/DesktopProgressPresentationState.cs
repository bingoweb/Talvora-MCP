using Talvora.Shared;

namespace Talvora.Tray;

internal enum DesktopProgressPresentationDecision
{
    Accept,
    SuppressRetired,
    SuppressOutOfOrder,
    SuppressDismissed,
}

internal sealed class DesktopProgressPresentationState
{
    private const int MaximumRetiredOperationIds = 512;

    private readonly Dictionary<string, long> _lastSequenceByOperation =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _dismissedWorklogOperationIds =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _retiredOperationIds =
        new(StringComparer.Ordinal);
    private readonly Queue<string> _retiredOperationOrder = new();

    public DesktopProgressPresentationDecision Evaluate(
        DesktopProgressMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_retiredOperationIds.Contains(message.OperationId))
        {
            return DesktopProgressPresentationDecision.SuppressRetired;
        }

        if (message.Sequence > 0)
        {
            if (_lastSequenceByOperation.TryGetValue(
                    message.OperationId,
                    out var lastSequence) &&
                message.Sequence <= lastSequence)
            {
                return DesktopProgressPresentationDecision.SuppressOutOfOrder;
            }

            _lastSequenceByOperation[message.OperationId] =
                message.Sequence;
        }

        if (message.Lane == DesktopProgressLane.Worklog &&
            _dismissedWorklogOperationIds.Contains(message.OperationId))
        {
            if (IsTerminal(message.Kind))
            {
                Retire(message.OperationId);
            }

            return DesktopProgressPresentationDecision.SuppressDismissed;
        }

        if (IsTerminal(message.Kind))
        {
            Retire(message.OperationId);
        }

        return DesktopProgressPresentationDecision.Accept;
    }

    public void MarkManualDismissed(
        string operationId,
        DesktopProgressLane lane,
        bool isTerminal)
    {
        if (lane == DesktopProgressLane.Worklog &&
            !isTerminal &&
            !string.IsNullOrWhiteSpace(operationId))
        {
            _dismissedWorklogOperationIds.Add(operationId);
        }
    }

    public static void AssertContract()
    {
        var state = new DesktopProgressPresentationState();
        var started = Create(
            "generation-a",
            DesktopProgressKind.Started,
            sequence: 1);
        if (state.Evaluate(started) !=
            DesktopProgressPresentationDecision.Accept)
        {
            throw new InvalidOperationException(
                "Desktop progress presentation rejected the initial worklog frame.");
        }

        if (state.Evaluate(started) !=
            DesktopProgressPresentationDecision.SuppressOutOfOrder)
        {
            throw new InvalidOperationException(
                "Desktop progress presentation did not suppress a duplicate sequence.");
        }

        var running = started with
        {
            Kind = DesktopProgressKind.Running,
            Sequence = 2,
        };
        if (state.Evaluate(running) !=
            DesktopProgressPresentationDecision.Accept)
        {
            throw new InvalidOperationException(
                "Desktop progress presentation rejected a monotonic worklog update.");
        }

        var completed = running with
        {
            Kind = DesktopProgressKind.Completed,
            Sequence = 4,
        };
        if (state.Evaluate(completed) !=
                DesktopProgressPresentationDecision.Accept ||
            state.Evaluate(running with { Sequence = 3 }) !=
                DesktopProgressPresentationDecision.SuppressRetired)
        {
            throw new InvalidOperationException(
                "Desktop progress terminal tombstone did not suppress a late heartbeat.");
        }

        var dismissed = Create(
            "generation-b",
            DesktopProgressKind.Started,
            sequence: 10);
        if (state.Evaluate(dismissed) !=
            DesktopProgressPresentationDecision.Accept)
        {
            throw new InvalidOperationException(
                "Desktop progress presentation rejected a new generation.");
        }

        state.MarkManualDismissed(
            dismissed.OperationId,
            DesktopProgressLane.Worklog,
            isTerminal: false);
        if (state.Evaluate(
                dismissed with
                {
                    Kind = DesktopProgressKind.Running,
                    Sequence = 11,
                }) !=
            DesktopProgressPresentationDecision.SuppressDismissed)
        {
            throw new InvalidOperationException(
                "Desktop progress manual dismissal did not suppress the same generation.");
        }

        var nextGeneration = Create(
            "generation-c",
            DesktopProgressKind.Started,
            sequence: 12);
        if (state.Evaluate(nextGeneration) !=
            DesktopProgressPresentationDecision.Accept)
        {
            throw new InvalidOperationException(
                "Desktop progress manual dismissal leaked into the next generation.");
        }

        static DesktopProgressMessage Create(
            string operationId,
            DesktopProgressKind kind,
            long sequence) =>
            new(
                operationId,
                "self-test",
                "Self-test",
                "Self-test",
                kind,
                DateTimeOffset.UtcNow,
                ElapsedSeconds: 0,
                Lane: DesktopProgressLane.Worklog,
                Sequence: sequence);
    }

    private void Retire(string operationId)
    {
        if (!_retiredOperationIds.Add(operationId))
        {
            return;
        }

        _retiredOperationOrder.Enqueue(operationId);
        while (_retiredOperationOrder.Count > MaximumRetiredOperationIds)
        {
            var expired = _retiredOperationOrder.Dequeue();
            _retiredOperationIds.Remove(expired);
            _dismissedWorklogOperationIds.Remove(expired);
            _lastSequenceByOperation.Remove(expired);
        }
    }

    private static bool IsTerminal(DesktopProgressKind kind) =>
        kind is
            DesktopProgressKind.Completed or
            DesktopProgressKind.Failed or
            DesktopProgressKind.Cancelled or
            DesktopProgressKind.Info or
            DesktopProgressKind.Warning;
}
