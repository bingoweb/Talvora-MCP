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
        DateTimeOffset? codeChangedUtc = null)
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
            ? $"{frame.Header.ActorName} {FormatAge(DateTimeOffset.UtcNow - frame.TimestampUtc)} yeni bilgi göndermedi · son: {frame.TimestampUtc.ToLocalTime():HH:mm:ss}"
            : frame.Current.FactLine;
        model.TaskNote = frame.Header.TaskNote;
        model.Purpose = stale ? null : frame.Current.Purpose;
        model.SetChanges(stale || frame.Current.Changes is null
            ? null
            : frame.Current.Changes.Select(change => (change.File, change.Added, change.Removed, change.Places)).ToArray());
        model.OutcomeReason = stale ? null : frame.Current.Outcome?.PlainReason;
        model.OutcomeNextStep = stale ? null : frame.Current.Outcome?.NextStep;

        // A session of one step: the single timeline row would only repeat the title (§7 polish, audit 12).
        var timeline = frame.Timeline.Count == 1 &&
            string.Equals(frame.Timeline[0].Label, frame.Current.Title, StringComparison.Ordinal)
                ? []
                : frame.Timeline;
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
                entry.Label))
            .ToArray());

        model.Footer = FormatFooter(frame.Totals);

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
