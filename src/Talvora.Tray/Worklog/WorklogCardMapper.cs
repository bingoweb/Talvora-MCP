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

    public static void Apply(
        WorklogCardViewModel model,
        DesktopWorklogFrame frame,
        string machineName,
        bool stale,
        DateTimeOffset? codeChangedUtc = null,
        WorklogDetails? details = null)
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
                entry.DurationSeconds))
            .ToArray());

        model.SignalSpanSeconds = SignalSpan(frame);
        model.SetSignal(BuildSignal(frame));
        model.SignalCaption = frame.Totals.Steps > frame.Timeline.Count
            ? $"── son {frame.Timeline.Count} adım "
            : "── oturum sinyali ";
        model.Footer = FormatFooter(frame.Totals);
        model.SessionName = $"0:{(string.IsNullOrWhiteSpace(project) ? "talvora" : project)}*";
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
    /// Session signal (§7b): every step with a measured duration (WL1) sits on the session's real time axis.
    /// Axis start = frame time − measured session seconds; a step ends at its timestamp and began its measured duration
    /// earlier. Steps without a measurement are not drawn; nothing is interpolated.
    /// </summary>
    public static IReadOnlyList<SignalPeak> BuildSignal(DesktopWorklogFrame frame)
    {
        var (start, span) = SignalAxis(frame);
        if (span <= 0)
        {
            return [];
        }

        var peaks = new List<SignalPeak>();
        foreach (var entry in frame.Timeline.Reverse())
        {
            if (entry.DurationSeconds is not { } seconds)
            {
                continue;
            }

            var end = Math.Clamp((entry.TimestampUtc - start).TotalSeconds / span, 0, 1);
            var begin = Math.Clamp(end - seconds / span, 0, end);
            peaks.Add(new SignalPeak(begin, end - begin, WorklogMeasure.BarWidth(seconds) / 56, entry.State switch
            {
                WorklogStepState.Succeeded => WorklogRowState.Succeeded,
                WorklogStepState.Failed => WorklogRowState.Failed,
                WorklogStepState.Cancelled => WorklogRowState.Cancelled,
                WorklogStepState.Running => WorklogRowState.Running,
                _ => WorklogRowState.Muted,
            }));
        }

        return peaks;
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
