using System.Globalization;
using Talvora.Shared;

namespace Talvora.Tray.Worklog;

/// <summary>
/// Maps a real protocol-v4 frame (N2) onto the card view model. Pure function of its inputs:
/// nothing is invented, missing data simply hides the corresponding row.
/// </summary>
public static class WorklogCardMapper
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    internal static void Apply(
        WorklogCardViewModel model,
        DesktopWorklogFrame frame,
        string machineName,
        bool stale,
        DateTimeOffset? codeChangedUtc = null,
        WorklogDetails? details = null,
        IReadOnlyList<WorklogSessionTab>? sessionTabs = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(frame);

        var project = frame.Header.ProjectName;
        model.Prompt = string.IsNullOrWhiteSpace(project)
            ? $"talvora@{machineName}:~"
            : $"talvora@{machineName}:~/{project}";
        model.Actor = frame.Header.ActorName.Replace(" · ", "·", StringComparison.Ordinal).ToLower(Turkish);

        model.State = stale
            ? WorklogVisualState.Waiting
            : frame.Current.State switch
            {
                WorklogStepState.Succeeded => WorklogVisualState.Succeeded,
                WorklogStepState.Failed => WorklogVisualState.Failed,
                WorklogStepState.Cancelled => WorklogVisualState.Cancelled,
                WorklogStepState.Waiting => WorklogVisualState.Waiting,
                _ => WorklogVisualState.Running,
            };
        model.Title = stale ? "Yanıt bekleniyor" : frame.Current.Title;
        model.FactLine = stale
            ? $"{frame.Header.ActorName} {FormatAge(DateTimeOffset.UtcNow - frame.TimestampUtc)} yeni bilgi göndermedi · son:\u00a0{frame.TimestampUtc.ToLocalTime():HH:mm:ss}"
            : frame.Current.FactLine;
        model.TaskNote = frame.Header.TaskNote;
        model.Purpose = stale ? null : frame.Current.Purpose;
        model.SetChanges(stale || frame.Current.Changes is null
            ? null
            : frame.Current.Changes.Select(change => (change.File, change.Added, change.Removed, change.Places)).ToArray());
        model.OutcomeReason = stale ? null : frame.Current.Outcome?.PlainReason;
        model.OutcomeNextStep = stale ? null : frame.Current.Outcome?.NextStep;

        // A finished step is already the command line: its history row would only repeat it, so the row is folded
        // into the command line together with its measured duration (§7b). A one-step session folds the same way.
        IReadOnlyList<WorklogTimelineEntry> timeline = frame.Timeline;
        double? finishedSeconds = null;
        if (frame.Timeline.Count > 0 &&
            string.Equals(frame.Timeline[0].Label, frame.Current.Title, StringComparison.Ordinal) &&
            (frame.Timeline.Count == 1 || (!stale && frame.Current.State != WorklogStepState.Running)))
        {
            finishedSeconds = frame.Timeline[0].DurationSeconds;
            timeline = frame.Timeline.Skip(1).ToArray();
        }

        model.FinishedSeconds = stale ? null : finishedSeconds;
        model.SetTimeline(timeline
            .Select(entry => new WorklogTimelineRow(
                entry.State switch
                {
                    WorklogStepState.Running => WorklogRowState.Running,
                    WorklogStepState.Succeeded => WorklogRowState.Succeeded,
                    WorklogStepState.Failed => WorklogRowState.Failed,
                    WorklogStepState.Cancelled => WorklogRowState.Cancelled,
                    _ => WorklogRowState.Muted,
                },
                entry.TimestampUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture),
                entry.Label,
                entry.DurationSeconds,
                entry.Purpose))
            .ToArray());

        model.SignalSpanSeconds = SignalSpan(frame);
        model.SetSignal(BuildSignal(frame));
        model.SignalLegend = model.HasSignal ? BuildSignalLegend(frame) : null;
        model.SignalCaption = frame.Totals.Steps > frame.Timeline.Count
            ? $"── son {frame.Timeline.Count} adım "
            : "── oturum ";
        model.Footer = FormatFooter(frame.Totals);
        model.WindowList = MapWindowList(sessionTabs ?? [], project);
        model.Steps = frame.Totals.Steps;
        model.FilesChanged = frame.Totals.FilesChanged;
        model.SessionSeconds = frame.Totals.SessionSeconds;
        model.ClockAnchorUtc = frame.TimestampUtc;
        model.SetDetails(details is null ? [] : BuildDetailRows(details));

        var preview = frame.Evidence?.CodePreview;
        if (string.IsNullOrWhiteSpace(preview))
        {
            model.SetCode(string.Empty, [], []);
        }
        else
        {
            var (files, lines) = UnifiedDiffViewParser.Parse(preview);
            model.SetCode(
                $"diff · {(codeChangedUtc ?? frame.TimestampUtc).ToLocalTime():HH:mm:ss}",
                files,
                lines);
        }
    }

    /// <summary>
    /// The status bar's window list (§7b "Window list", D109v): one tmux-style entry per open session, the shown one
    /// plus up to three others. A session the tray has not observed is never invented: without a shown tab the
    /// shown entry falls back to the frame's own project.
    /// </summary>
    internal static WorklogWindowList MapWindowList(
        IReadOnlyList<WorklogSessionTab> tabs,
        string? shownProjectName)
    {
        ArgumentNullException.ThrowIfNull(tabs);

        if (!tabs.Any(static tab => tab.IsShown))
        {
            var name = string.IsNullOrWhiteSpace(shownProjectName) ? FallbackProjectName : shownProjectName.Trim();
            var shownDisplay = CutName(name);
            return new WorklogWindowList(
                [new WorklogWindowEntry(0, string.Empty, $"0:{shownDisplay}*", true, 0, $"{name}, bu kart", null)],
                1,
                $"Açık oturumlar: {name}, bu kart");
        }

        var ordered = tabs.OrderBy(static tab => tab.Index).ToArray();
        var shownTab = ordered.First(static tab => tab.IsShown);
        var baseNames = ordered.Select(static tab => BaseName(tab.ProjectName)).ToArray();
        var named = ordered.Select((tab, position) =>
        {
            var baseName = baseNames[position];
            var isShown = ReferenceEquals(tab, shownTab);
            var duplicate = baseNames.Count(other => string.Equals(other, baseName, StringComparison.OrdinalIgnoreCase)) > 1;
            var actor = duplicate ? ActorWord(tab.ActorName) : string.Empty;
            var display = CutName(baseName);
            var spoken = baseName;
            if (actor.Length > 0)
            {
                display += "·" + actor;
                spoken += " (" + actor + ")";
            }

            var flag = isShown
                ? "*"
                : tab.State switch
                {
                    WorklogSessionTabState.Running => "#",
                    WorklogSessionTabState.Silent => "~",
                    WorklogSessionTabState.Failed => "!",
                    _ => string.Empty,
                };
            var stateWord = isShown
                ? "bu kart"
                : tab.State switch
                {
                    WorklogSessionTabState.Running => "sürüyor",
                    WorklogSessionTabState.Silent => "sessiz",
                    WorklogSessionTabState.Succeeded => "tamamlandı",
                    WorklogSessionTabState.Failed => "hata verdi",
                    _ => "iptal edildi",
                };
            return (Tab: tab, IsShown: isShown, Text: $"{tab.Index}:{display}{flag}", Spoken: $"{spoken}, {stateWord}");
        }).ToArray();

        // Priority of the others: Running, Silent, Failed, then the rest; ties by index (the input is in index order).
        var others = named
            .Where(static item => !item.IsShown)
            .Select(static (item, position) => (Item: item, Position: position))
            .OrderBy(static pair => pair.Item.Tab.State switch
            {
                WorklogSessionTabState.Running => 0,
                WorklogSessionTabState.Silent => 1,
                WorklogSessionTabState.Failed => 2,
                _ => 3,
            })
            .ThenBy(static pair => pair.Position)
            .Take(MaximumOtherEntries)
            .Select(static (pair, rank) => (pair.Item, Rank: rank + 1))
            .ToArray();

        var shownItem = named.First(static item => item.IsShown);
        var entries = new List<(int Index, WorklogWindowEntry Entry)>
        {
            (shownItem.Tab.Index, ToEntry(shownItem, 0)),
        };
        entries.AddRange(others.Select(pair => (pair.Item.Tab.Index, ToEntry(pair.Item, pair.Rank))));

        return new WorklogWindowList(
            entries.OrderBy(static pair => pair.Index).Select(static pair => pair.Entry).ToArray(),
            tabs.Count,
            "Açık oturumlar: " + string.Join("; ", named.Select(static item => item.Spoken)));

        static WorklogWindowEntry ToEntry(
            (WorklogSessionTab Tab, bool IsShown, string Text, string Spoken) item,
            int rank) =>
            new(
                item.Tab.Index,
                item.Tab.SessionId,
                item.Text,
                item.IsShown,
                rank,
                item.Spoken,
                item.IsShown ? null : "Kartı bu oturuma geçirir");
    }

    private const string FallbackProjectName = "talvora";
    private const int MaximumNameLength = 16;
    private const int MaximumOtherEntries = 3;

    /// <summary>A name over 16 text elements keeps its first 15 and ends in "…"; a surrogate pair or a combining mark is never split.</summary>
    private static string CutName(string name)
    {
        var elements = new StringInfo(name);
        return elements.LengthInTextElements > MaximumNameLength
            ? elements.SubstringByTextElements(0, MaximumNameLength - 1) + "…"
            : name;
    }

    private static string BaseName(string? projectName) =>
        string.IsNullOrWhiteSpace(projectName) ? FallbackProjectName : projectName.Trim();

    /// <summary>First word of the actor ("ChatGPT · Dev" → "chatgpt"); empty when there is none.</summary>
    private static string ActorWord(string? actorName) =>
        (actorName ?? string.Empty).Replace('·', ' ')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.ToLowerInvariant() ?? string.Empty;

    /// <summary>
    /// Session time ruler (§7b UI12c): every step with a measured duration (WL1) is a block on one linear real-time
    /// axis. Axis start = frame time − measured session seconds; a step ends at its timestamp and began its measured
    /// duration earlier. The running step is drawn from its measured elapsed time up to the frame time. Steps without
    /// a measurement are not drawn; nothing is interpolated.
    /// </summary>
    public static IReadOnlyList<SignalPeak> BuildSignal(DesktopWorklogFrame frame)
    {
        var (start, span) = SignalAxis(frame);
        if (span <= 0)
        {
            return [];
        }

        var peaks = new List<SignalPeak>();
        foreach (var (begin, end, state, label, seconds) in MeasuredIntervals(frame, start))
        {
            var from = Math.Clamp((begin - start).TotalSeconds / span, 0, 1);
            var to = Math.Clamp((end - start).TotalSeconds / span, from, 1);
            peaks.Add(new SignalPeak(from, to - from, state, label, seconds));
        }

        return peaks;
    }

    /// <summary>
    /// The ruler's printed numbers (UI12c). Work is the union of the measured intervals, so parallel steps count once.
    /// The gap (axis length − work) is printed only when every finished step on the axis was measured; otherwise the
    /// unmeasured steps would be miscounted as idle time. Under one measured second it is not printed (D104v): a session
    /// that was all work has no gap to show ("ara 0,0 sn").
    /// </summary>
    public static SignalLegend BuildSignalLegend(DesktopWorklogFrame frame)
    {
        var (start, span) = SignalAxis(frame);
        var intervals = span <= 0 ? [] : MeasuredIntervals(frame, start).ToArray();
        var work = 0d;
        var reach = start;
        foreach (var (begin, end, _, _, _) in intervals.OrderBy(interval => interval.Begin))
        {
            var from = begin < reach ? reach : begin;
            if (end > from)
            {
                work += (end - from).TotalSeconds;
                reach = end;
            }
        }

        var failures = intervals.Count(interval => interval.State == WorklogRowState.Failed);
        // UI12d: count finished steps like the status bar; a running step is named separately.
        var running = intervals.Count(interval => interval.State == WorklogRowState.Running);
        var finished = intervals.Length - running;
        var complete = frame.Timeline.All(entry => entry.DurationSeconds is not null || entry.State == WorklogStepState.Running);
        var gap = Math.Max(0, span - work);
        return new SignalLegend(
            start.ToLocalTime().DateTime,
            $"iş {WorklogMeasure.Format(work)} · {finished} adım" + (running > 0 ? $" · {running} sürüyor" : string.Empty),
            failures > 0 ? $"{failures} hata" : null,
            complete && span > 0 && gap >= 1 ? $"ara {WorklogMeasure.Format(gap)}" : null);
    }

    private static IEnumerable<(DateTimeOffset Begin, DateTimeOffset End, WorklogRowState State, string Label, double Seconds)> MeasuredIntervals(
        DesktopWorklogFrame frame, DateTimeOffset start)
    {
        foreach (var entry in frame.Timeline.Reverse())
        {
            if (entry.DurationSeconds is not { } seconds)
            {
                continue;
            }

            var begin = entry.TimestampUtc.AddSeconds(-seconds);
            yield return (begin < start ? start : begin, entry.TimestampUtc, entry.State switch
            {
                WorklogStepState.Succeeded => WorklogRowState.Succeeded,
                WorklogStepState.Failed => WorklogRowState.Failed,
                WorklogStepState.Cancelled => WorklogRowState.Cancelled,
                WorklogStepState.Running => WorklogRowState.Running,
                _ => WorklogRowState.Muted,
            }, entry.Label, seconds);
        }

        if (frame.Current.State == WorklogStepState.Running && frame.Current.ElapsedSeconds > 0)
        {
            var begin = frame.TimestampUtc.AddSeconds(-frame.Current.ElapsedSeconds);
            yield return (begin < start ? start : begin, frame.TimestampUtc, WorklogRowState.Running, frame.Current.Title, frame.Current.ElapsedSeconds);
        }
    }

    /// <summary>Real length of the strip's axis in seconds (0 when the session has no measured length).</summary>
    public static double SignalSpan(DesktopWorklogFrame frame) => SignalAxis(frame).Span;

    private static (DateTimeOffset Start, double Span) SignalAxis(DesktopWorklogFrame frame)
    {
        var span = frame.Totals.SessionSeconds;
        if (span <= 0)
        {
            return (frame.TimestampUtc, 0);
        }

        var start = frame.TimestampUtc.AddSeconds(-span);

        // Only the newest 30 steps travel in a frame. When older ones fell out, the axis starts where the oldest kept
        // step began, so empty space never suggests an idle period that really had work in it.
        if (frame.Totals.Steps > frame.Timeline.Count)
        {
            var kept = frame.Timeline
                .Where(entry => entry.DurationSeconds is not null)
                .Select(entry => entry.TimestampUtc.AddSeconds(-entry.DurationSeconds!.Value))
                .DefaultIfEmpty(start)
                .Min();
            if (kept > start)
            {
                start = kept;
                span = Math.Max(0.001, (frame.TimestampUtc - start).TotalSeconds);
            }
        }

        return (start, span);
    }

    /// <summary>
    /// "ayrıntılar" rows (N6). The technical tool name appears only here, never in title, fact or timeline.
    /// Values come from the received message, which the protocol has already redacted.
    /// </summary>
    public static IReadOnlyList<WorklogDetailRow> BuildDetailRows(WorklogDetails details)
    {
        var rows = new List<WorklogDetailRow>();
        var technical = details.ToolName is "search" or "fetch" || details.ToolName.StartsWith("talvora_", StringComparison.Ordinal)
            ? details.ToolName
            : "talvora_" + details.ToolName;
        var title = Talvora.Shared.TalvoraToolTitles.Get(technical);
        rows.Add(new("adım", title is null ? technical : $"{title} ({technical})"));
        if (!string.IsNullOrWhiteSpace(details.Result))
        {
            var result = details.Result.StartsWith("Sonuç: ", StringComparison.Ordinal) ? details.Result[7..] : details.Result;
            rows.Add(new("sonuç", result.Replace("  //  ", " · ", StringComparison.Ordinal)));
        }

        if (details.Files.Count > 0)
        {
            var shown = string.Join(", ", details.Files.Take(8).Select(file => System.IO.Path.GetFileName(file.Replace('\\', '/'))));
            rows.Add(new("dosyalar", details.Files.Count > 8 ? $"{shown} +{details.Files.Count - 8}" : shown));
        }

        rows.Add(new("süre", FormatDuration(details.ElapsedSeconds)));
        rows.Add(new("oturum", $"{details.SessionId[..Math.Min(8, details.SessionId.Length)]} · #{details.Sequence}"));
        return rows;
    }

    public static string FormatDuration(double seconds)
    {
        var value = Math.Max(0, seconds);
        return value < 60
            ? value.ToString("0.0", Turkish) + " sn"
            : FormatClock(value);
    }

    public static string FormatFooter(WorklogTotals totals)
    {
        var parts = new List<string>();
        if (totals.Steps > 0)
        {
            parts.Add($"{totals.Steps} adım");
        }

        parts.Add(FormatClock(totals.SessionSeconds));
        if (totals.FilesChanged > 0)
        {
            parts.Add($"{totals.FilesChanged} dosya değişti");
        }

        return string.Join(" · ", parts);
    }

    public static string FormatClock(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.TotalHours >= 1
            ? span.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : span.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private static string FormatAge(TimeSpan age)
    {
        var total = Math.Max(0, (int)age.TotalSeconds);
        return total >= 60 ? $"{total / 60} dk {total % 60} sn'dir" : $"{total} sn'dir";
    }
}
