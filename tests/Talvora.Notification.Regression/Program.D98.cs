using Talvora.Shared;

internal static partial class Program
{
    private static void AssertD98InterleavedCodeSessions()
    {
        var changedA = new DateTimeOffset(2026, 11, 16, 11, 0, 0, TimeSpan.Zero);
        var changedB = changedA.AddMinutes(1);
        const string previewA = "@@ FILE A.cs\n-old\n+new A\n";
        const string previewB = "@@ FILE B.cs\n-old\n+new B\n";

        static DesktopProgressMessage Frame(
            string sessionId, string title, DateTimeOffset timestamp,
            DesktopProgressEvidence? evidence)
        {
            var frame = new DesktopWorklogFrame(
                sessionId, 1, timestamp,
                new WorklogSessionHeader("Demo", "ChatGPT"),
                new WorklogStep(WorklogStepCategory.SourceEdit, WorklogStepState.Succeeded,
                    title, null, null, 0),
                [],
                new WorklogTotals(0, 0, 0, WorklogCheckState.Unknown, WorklogCheckState.Unknown),
                evidence, 60);
            return new DesktopProgressMessage(
                "operation-" + sessionId, "talvora_apply_patch", title, "",
                DesktopProgressKind.Completed, timestamp, 0, evidence,
                WorklogFrame: frame);
        }

        static void Expect(
            DesktopProgressCodeState cache, DesktopProgressMessage message,
            string? preview, DateTimeOffset? changeTime, string? title, string step)
        {
            var restored = cache.Restore(message);
            if (restored.Evidence?.CodePreview != preview ||
                restored.WorklogFrame?.Evidence?.CodePreview != preview ||
                cache.CodeChangedUtc != changeTime ||
                cache.Title != title)
                throw new InvalidOperationException(
                    $"D98 RED {step}: preview '{restored.Evidence?.CodePreview}', " +
                    $"frame '{restored.WorklogFrame?.Evidence?.CodePreview}', " +
                    $"time '{cache.CodeChangedUtc}', title '{cache.Title}'.");
        }

        var cache = new DesktopProgressCodeState();
        var aPreview = new DesktopProgressEvidence(
            AddedLines: 2, RemovedLines: 1, CodePreview: previewA, CodeChangeId: "c1");
        var bPreview = new DesktopProgressEvidence(
            AddedLines: 3, RemovedLines: 2, CodePreview: previewB, CodeChangeId: "c2");
        var aIdentity = new DesktopProgressEvidence(CodeChangeId: "c1");
        var bIdentity = new DesktopProgressEvidence(CodeChangeId: "c2");

        // (a) An unrelated session without code must not erase A's saved preview.
        Expect(cache, Frame("A", "Change A", changedA, aPreview),
            previewA, changedA, "Change A", "A initial");
        Expect(cache, Frame("A", "Later A", changedA.AddSeconds(10), aIdentity),
            previewA, changedA, "Change A", "A same-session id-only");
        Expect(cache, Frame("B", "Inspect B", changedB, null),
            null, null, null, "B without code");
        Expect(cache, Frame("A", "Later A", changedB.AddSeconds(10), aIdentity),
            previewA, changedA, "Change A", "A after B");

        // (b) Interleaved sessions can each restore their own id-only frame and change time.
        Expect(cache, Frame("B", "Change B", changedB, bPreview),
            previewB, changedB, "Change B", "B initial");
        Expect(cache, Frame("A", "Later A", changedB.AddSeconds(20), aIdentity),
            previewA, changedA, "Change A", "A after B code");
        Expect(cache, Frame("B", "Later B", changedB.AddSeconds(30), bIdentity),
            previewB, changedB, "Change B", "B after A code");

        // (c) Clearing A without an identity does not clear B's code.
        Expect(cache, Frame("A", "A no identity", changedB.AddSeconds(40), null),
            null, null, null, "A cleared");
        Expect(cache, Frame("B", "Later B", changedB.AddSeconds(50), bIdentity),
            previewB, changedB, "Change B", "B retained after A cleared");
        Expect(cache, Frame("A", "A old identity", changedB.AddSeconds(60), aIdentity),
            null, null, null, "A remains cleared");

        // The LRU contains at most 16 sessions and refreshes recency on each restore.
        var lru = new DesktopProgressCodeState();
        for (var i = 0; i < 16; i++)
            lru.Restore(Frame("s" + i, "Change " + i, changedA.AddMinutes(i),
                new DesktopProgressEvidence(CodePreview: "preview " + i,
                    CodeChangeId: "id-" + i)));
        Expect(lru, Frame("s0", "Recall s0", changedB.AddMinutes(18),
            new DesktopProgressEvidence(CodeChangeId: "id-0")),
            "preview 0", changedA, "Change 0", "recently used s0");
        lru.Restore(Frame("s16", "Change 16", changedB.AddMinutes(19),
            new DesktopProgressEvidence(CodePreview: "preview 16",
                CodeChangeId: "id-16")));
        Expect(lru, Frame("s1", "Evicted s1", changedB.AddMinutes(20),
            new DesktopProgressEvidence(CodeChangeId: "id-1")),
            null, null, null, "least recently used s1 evicted");
        Expect(lru, Frame("s0", "Recall s0", changedB.AddMinutes(21),
            new DesktopProgressEvidence(CodeChangeId: "id-0")),
            "preview 0", changedA, "Change 0", "recently used s0 survives");

        // A message without a worklog frame uses OperationId as its session key.
        var fallback = new DesktopProgressCodeState();
        var standalone = new DesktopProgressMessage(
            "operation-fallback", "talvora_apply_patch", "Fallback change", "",
            DesktopProgressKind.Completed, changedA, 0, aPreview);
        fallback.Restore(standalone);
        var recovered = fallback.Restore(standalone with
        {
            TimestampUtc = changedB,
            Evidence = aIdentity,
        });
        if (recovered.Evidence?.CodePreview != previewA ||
            fallback.CodeChangedUtc != changedA)
            throw new InvalidOperationException("D98 RED: OperationId fallback lost code.");

        Console.WriteLine("D98_INTERLEAVED_CODE_TERMINAL_GREEN");
    }
}
