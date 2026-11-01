using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Talvora.Tray.Worklog;

/// <summary>Visual state of the whole card (DESIGN-WORKLOG §7).</summary>
public enum WorklogVisualState
{
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Waiting,
}

/// <summary>State of one timeline row.</summary>
public enum WorklogRowState
{
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Muted,
}

/// <summary>Kind of one rendered code-terminal line.</summary>
public enum DiffLineKind
{
    File,
    Hunk,
    Context,
    Added,
    Removed,
    Note,
}

/// <summary>
/// One shell-history row (DESIGN-WORKLOG §7b). <see cref="DurationSeconds"/> is the measured stopwatch time of that
/// tool call (WL1); null means nothing was measured, and then neither a number nor a bar is drawn.
/// </summary>
public sealed record WorklogTimelineRow(WorklogRowState State, string Time, string Label, double? DurationSeconds = null)
{
    public string Tag => State switch
    {
        WorklogRowState.Running => "▸",
        WorklogRowState.Succeeded => "✓",
        WorklogRowState.Failed => "✗",
        WorklogRowState.Cancelled => "■",
        _ => "·",
    };

    public bool HasDuration => DurationSeconds is not null;

    public string DurationText => DurationSeconds is { } seconds ? WorklogMeasure.Format(seconds) : string.Empty;

    /// <summary>Signal bar width on the fixed log scale of §7b; 0 when nothing was measured.</summary>
    public double BarWidth => DurationSeconds is { } seconds ? WorklogMeasure.BarWidth(seconds) : 0;
}

/// <summary>
/// One peak of the session signal strip (§7b "oturum sinyali"): where a measured step ran on the session's real time
/// axis (fractions of the session length) and its height on the same fixed log scale as the history bars.
/// </summary>
public sealed record SignalPeak(double StartFraction, double WidthFraction, double HeightFraction, WorklogRowState State);

/// <summary>Formats and scales measured durations (DESIGN-WORKLOG §7b). Pure; never estimates.</summary>
public static class WorklogMeasure
{
    private static readonly System.Globalization.CultureInfo Turkish = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>"0,9 sn" · "41 sn" · "1:05" · "1:02:07".</summary>
    public static string Format(double seconds)
    {
        var value = Math.Max(0, seconds);
        if (value < 10)
        {
            return value.ToString("0.0", Turkish) + " sn";
        }

        if (value < 60)
        {
            return Math.Floor(value).ToString("0", Turkish) + " sn";
        }

        var span = TimeSpan.FromSeconds(Math.Floor(value));
        return span.TotalHours >= 1
            ? span.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
            : span.ToString(@"m\:ss", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>width = round(56 · log10(1 + 10·s) / log10(1 + 6000)), clamped to 2–56 DIP.</summary>
    public static double BarWidth(double seconds) =>
        Math.Clamp(Math.Round(56 * Math.Log10(1 + 10 * Math.Max(0, seconds)) / Math.Log10(6001)), 2, 56);

    /// <summary>Session clock as "mm:ss" / "h:mm:ss".</summary>
    public static string Clock(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Floor(Math.Max(0, seconds)));
        return span.TotalHours >= 1
            ? span.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
            : span.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
    }
}

public sealed record DiffFileTab(string Name, int Added, int Removed)
{
    public string Counts => $"+{Added} −{Removed}";
}

/// <summary>
/// One code-terminal row. EmphasisStart/EmphasisLength mark the characters that really changed between a
/// removed line and its paired added line (word-level highlight, DESIGN-WORKLOG §6.3); -1 = no pair.
/// </summary>
public sealed record DiffLine(DiffLineKind Kind, string Number, string Text, int EmphasisStart = -1, int EmphasisLength = 0)
{
    public string Marker => Kind switch
    {
        DiffLineKind.Added => "+",
        DiffLineKind.Removed => "−",
        DiffLineKind.File => "▌",
        _ => " ",
    };

    public string Prefix => EmphasisStart < 0 ? Text : Text[..EmphasisStart];

    public string Emphasis => EmphasisStart < 0 ? string.Empty : Text.Substring(EmphasisStart, EmphasisLength);

    public string Suffix => EmphasisStart < 0 ? string.Empty : Text[(EmphasisStart + EmphasisLength)..];
}

/// <summary>One "ayrıntılar" row: faint label, technical value (N6, DESIGN-WORKLOG §3 expanded view).</summary>
public sealed record WorklogDetailRow(string Label, string Value);

/// <summary>
/// Technical evidence of the current step, only from the received message (already redacted by the protocol):
/// tool title + technical name, result line, files, real duration, session identity.
/// </summary>
public sealed record WorklogDetails(
    string ToolName, string? Result, IReadOnlyList<string> Files, double ElapsedSeconds, string SessionId, long Sequence)
{
    public static WorklogDetails From(Talvora.Shared.DesktopProgressMessage message) =>
        new(message.ToolName, message.Evidence?.Result, message.Evidence?.Files ?? [],
            message.ElapsedSeconds, message.WorklogFrame?.SessionId ?? message.OperationId, message.Sequence);
}

/// <summary>Per-user memory of the "ayrıntılar" expander (N6). Best effort; never fails the card.</summary>
public static class WorklogDetailsPreference
{
    private static readonly string FilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Talvora", "ControlCenter", "worklog-details-open.flag");

    public static bool Load()
    {
        try
        {
            return System.IO.File.Exists(FilePath);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void Save(bool open)
    {
        try
        {
            if (open)
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
                System.IO.File.WriteAllText(FilePath, "1");
            }
            else if (System.IO.File.Exists(FilePath))
            {
                System.IO.File.Delete(FilePath);
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // The expander still works for this card; only the memory is lost.
        }
    }
}

/// <summary>"nerede" row: a changed file and the declarations its hunks touched (real, §6.2).</summary>
public sealed record WorklogChangeRow(string File, string Places)
{
    public bool HasPlaces => Places.Length > 0;
}

/// <summary>
/// Everything the worklog card shows. Filled only from real frames (no fabricated telemetry).
/// </summary>
public sealed class WorklogCardViewModel : INotifyPropertyChanged
{
    private const int VisibleTimelineRows = 5;
    private const int VisibleChangeRows = 3;

    private WorklogVisualState state;
    private string prompt = string.Empty;
    private string actor = string.Empty;
    private string title = string.Empty;
    private string? factLine;
    private string? taskNote;
    private string? purpose;
    private string? changeAdded;
    private string? changeRemoved;
    private string? changeFiles;
    private string? moreChangedFiles;
    private string? outcomeReason;
    private string? outcomeNextStep;
    private string? moreSteps;
    private string footer = string.Empty;
    private string codeHeader = string.Empty;
    private bool animationsEnabled = true;
    private bool hasDetails;
    private bool isDetailsOpen;
    private double? finishedSeconds;
    private string signalCaption = "── oturum sinyali ";
    private double signalSpanSeconds;
    private string sessionName = string.Empty;
    private int steps;
    private int filesChanged;
    private double sessionSeconds;
    private DateTimeOffset? clockAnchorUtc;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<WorklogTimelineRow> Timeline { get; } = [];

    public ObservableCollection<DiffFileTab> CodeFiles { get; } = [];

    public ObservableCollection<DiffLine> CodeLines { get; } = [];

    /// <summary>"nerede" rows, at most three; the rest is counted in <see cref="MoreChangedFiles"/>.</summary>
    public ObservableCollection<WorklogChangeRow> ChangeRows { get; } = [];

    /// <summary>Session signal strip (§7b); only measured steps, so an empty list hides the strip.</summary>
    public ObservableCollection<SignalPeak> Signal { get; } = [];

    public bool HasSignal => Signal.Count > 0;

    /// <summary>"── oturum sinyali " or, when older steps fell out of the 30-step window, "── son 30 adım ".</summary>
    public string SignalCaption { get => signalCaption; set => Set(ref signalCaption, value); }

    /// <summary>Real length of the strip's time axis in seconds (for minute ticks); 0 = no ticks.</summary>
    public double SignalSpanSeconds { get => signalSpanSeconds; set => Set(ref signalSpanSeconds, value); }

    /// <summary>"ayrıntılar" rows (N6); empty hides the footer button.</summary>
    public ObservableCollection<WorklogDetailRow> DetailRows { get; } = [];

    /// <summary>"talvora@MACHINE:~/Project" — machine and project are real values.</summary>
    public string Prompt { get => prompt; set => Set(ref prompt, value); }

    /// <summary>"codex", "chatgpt·dev", "chatgpt·yönetim", "claude", "talvora".</summary>
    public string Actor { get => actor; set => Set(ref actor, value); }

    public WorklogVisualState State
    {
        get => state;
        set
        {
            if (Set(ref state, value))
            {
                OnPropertyChanged(nameof(StateLabel));
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsFinished));
                OnPropertyChanged(nameof(FinishedText));
            }
        }
    }

    public string StateLabel => State switch
    {
        WorklogVisualState.Running => "ÇALIŞIYOR",
        WorklogVisualState.Succeeded => "TAMAM",
        WorklogVisualState.Failed => "HATA",
        WorklogVisualState.Cancelled => "DURDU",
        _ => "BEKLİYOR",
    };

    public bool IsRunning => State == WorklogVisualState.Running;

    /// <summary>Any state but running: shows the quiet state line instead of the live line.</summary>
    public bool IsFinished => State != WorklogVisualState.Running;

    public string Title
    {
        get => title;
        set
        {
            if (Set(ref title, value))
            {
                OnPropertyChanged(nameof(TitleMain));
                OnPropertyChanged(nameof(TitleDetail));
            }
        }
    }

    /// <summary>"Kod değişikliği uygulandı" of "Kod değişikliği uygulandı · GitTools.cs" (semibold).</summary>
    public string TitleMain => Title.Split(" · ", 2)[0];

    /// <summary>" · GitTools.cs" (regular weight, muted) or empty.</summary>
    public string TitleDetail => Title.Contains(" · ", StringComparison.Ordinal) ? " · " + Title.Split(" · ", 2)[1] : string.Empty;

    public string? FactLine
    {
        get => factLine;
        set
        {
            if (Set(ref factLine, value))
            {
                OnPropertyChanged(nameof(ResultLine));
            }
        }
    }

    /// <summary>"# görev" line: the agent's task for this session (talvora_worklog_note).</summary>
    public string? TaskNote { get => taskNote; set => Set(ref taskNote, value); }

    /// <summary>"ne" row: the agent's own words for what this step changes (apply_patch purpose).</summary>
    public string? Purpose { get => purpose; set => Set(ref purpose, value); }

    /// <summary>"sonuç" row: the real fact line, unless "nerede/fark" already show the change.</summary>
    public string? ResultLine => HasChanges ? null : FactLine;

    public bool HasChanges => ChangeRows.Count > 0;

    /// <summary>"fark" row parts: "+12", "−3", "· 4 dosya".</summary>
    public string? ChangeAdded { get => changeAdded; private set => Set(ref changeAdded, value); }

    public string? ChangeRemoved { get => changeRemoved; private set => Set(ref changeRemoved, value); }

    public string? ChangeFiles { get => changeFiles; private set => Set(ref changeFiles, value); }

    /// <summary>"+2 dosya daha" or null.</summary>
    public string? MoreChangedFiles { get => moreChangedFiles; private set => Set(ref moreChangedFiles, value); }

    public string? OutcomeReason { get => outcomeReason; set => Set(ref outcomeReason, value); }

    public string? OutcomeNextStep { get => outcomeNextStep; set => Set(ref outcomeNextStep, value); }

    /// <summary>"+9 adım daha" or null.</summary>
    public string? MoreSteps { get => moreSteps; set => Set(ref moreSteps, value); }

    /// <summary>"14 adım · 06:12 · 5 dosya değişti" (zero parts omitted by the mapper).</summary>
    public string Footer { get => footer; set => Set(ref footer, value); }

    /// <summary>"diff · 14:02:11" — time of the real code change.</summary>
    public string CodeHeader
    {
        get => codeHeader;
        set
        {
            if (Set(ref codeHeader, value))
            {
                OnPropertyChanged(nameof(CodeTime));
            }
        }
    }

    /// <summary>"14:02:11" of <see cref="CodeHeader"/>, for the split-pane border line (§7b).</summary>
    public string CodeTime => CodeHeader.StartsWith("diff · ", StringComparison.Ordinal) ? CodeHeader[7..] : CodeHeader;

    /// <summary>Measured time of the finished current step (its history row, WL1); null = not measured.</summary>
    public double? FinishedSeconds
    {
        get => finishedSeconds;
        set
        {
            if (Set(ref finishedSeconds, value))
            {
                OnPropertyChanged(nameof(FinishedText));
            }
        }
    }

    /// <summary>"   41\u00a0sn\u00a0sürdü" after the command line (kept on one line), or empty.</summary>
    public string FinishedText => FinishedSeconds is { } seconds && IsFinished
        ? "   " + WorklogMeasure.Format(seconds).Replace(' ', '\u00a0') + "\u00a0sürdü"
        : string.Empty;

    /// <summary>tmux window name, "0:Classroom*".</summary>
    public string SessionName { get => sessionName; set => Set(ref sessionName, value); }

    /// <summary>Real step count of the session (Totals.Steps).</summary>
    public int Steps { get => steps; set => Set(ref steps, value); }

    /// <summary>Real changed-file count of the session (Totals.FilesChanged).</summary>
    public int FilesChanged { get => filesChanged; set => Set(ref filesChanged, value); }

    /// <summary>Measured session seconds at <see cref="ClockAnchorUtc"/> (Totals.SessionSeconds).</summary>
    public double SessionSeconds { get => sessionSeconds; set => Set(ref sessionSeconds, value); }

    /// <summary>When <see cref="SessionSeconds"/> was measured; null = no live clock (the value stays frozen).</summary>
    public DateTimeOffset? ClockAnchorUtc { get => clockAnchorUtc; set => Set(ref clockAnchorUtc, value); }

    /// <summary>Session seconds now: advances by wall clock from the measured value only while running.</summary>
    public double SessionSecondsAt(DateTimeOffset nowUtc) =>
        IsRunning && ClockAnchorUtc is { } anchor
            ? SessionSeconds + Math.Max(0, (nowUtc - anchor).TotalSeconds)
            : SessionSeconds;

    /// <summary>"03:41 · 7 adım · 3 dosya" — zero counts are omitted.</summary>
    public string StatusStats(DateTimeOffset nowUtc)
    {
        var parts = new List<string> { WorklogMeasure.Clock(SessionSecondsAt(nowUtc)) };
        if (Steps > 0)
        {
            parts.Add($"{Steps} adım");
        }

        if (FilesChanged > 0)
        {
            parts.Add($"{FilesChanged} dosya");
        }

        return string.Join(" · ", parts);
    }

    public bool HasCode => CodeLines.Count > 0;

    /// <summary>File tabs only help with two or more files; one file is already named by its header row.</summary>
    public bool HasFileTabs => CodeFiles.Count > 1;

    public bool HasTimeline => Timeline.Count > 0;

    public bool AnimationsEnabled { get => animationsEnabled; set => Set(ref animationsEnabled, value); }

    /// <summary>Shows the "ayrıntılar ▸" button. Stays false until card N6 provides the evidence surface.</summary>
    public bool HasDetails { get => hasDetails; set => Set(ref hasDetails, value); }

    /// <summary>Expander state; the window restores it from <see cref="WorklogDetailsPreference"/>.</summary>
    public bool IsDetailsOpen
    {
        get => isDetailsOpen;
        set
        {
            if (Set(ref isDetailsOpen, value))
            {
                OnPropertyChanged(nameof(DetailsToggleText));
                OnPropertyChanged(nameof(ShowDetails));
            }
        }
    }

    public bool ShowDetails => HasDetails && IsDetailsOpen;

    public string DetailsToggleText => IsDetailsOpen ? "ayrıntılar ▾" : "ayrıntılar ▸";

    /// <summary>Plain-text copy of the rows (already redacted values only).</summary>
    public string DetailsCopyText => string.Join(Environment.NewLine, DetailRows.Select(row => $"{row.Label,-9}{row.Value}"));

    public void SetDetails(IReadOnlyList<WorklogDetailRow> rows)
    {
        DetailRows.Clear();
        foreach (var row in rows)
        {
            DetailRows.Add(row);
        }

        HasDetails = DetailRows.Count > 0;
        OnPropertyChanged(nameof(ShowDetails));
    }

    /// <summary>Replaces the timeline with the newest rows (newest first), keeping the visible budget.</summary>
    public void SetTimeline(IReadOnlyList<WorklogTimelineRow> newestFirst)
    {
        Timeline.Clear();
        foreach (var row in newestFirst.Take(VisibleTimelineRows))
        {
            Timeline.Add(row);
        }

        var hidden = newestFirst.Count - Timeline.Count;
        MoreSteps = hidden > 0 ? $"+{hidden} adım daha" : null;
        OnPropertyChanged(nameof(HasTimeline));
    }

    /// <summary>Replaces "nerede/fark" with the real per-file changes of a committed edit; null hides both.</summary>
    public void SetChanges(IReadOnlyList<(string File, int Added, int Removed, IReadOnlyList<string> Places)>? changes)
    {
        ChangeRows.Clear();
        if (changes is { Count: > 0 })
        {
            foreach (var change in changes.Take(VisibleChangeRows))
            {
                ChangeRows.Add(new WorklogChangeRow(change.File, string.Join(", ", change.Places)));
            }
            ChangeAdded = "+" + changes.Sum(change => change.Added);
            ChangeRemoved = " −" + changes.Sum(change => change.Removed);
            ChangeFiles = $" · {changes.Count} dosya";
            MoreChangedFiles = changes.Count > VisibleChangeRows ? $"+{changes.Count - VisibleChangeRows} dosya daha" : null;
        }
        else
        {
            ChangeAdded = ChangeRemoved = ChangeFiles = MoreChangedFiles = null;
        }
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(ResultLine));
    }

    /// <summary>Replaces the session signal peaks (oldest first).</summary>
    public void SetSignal(IReadOnlyList<SignalPeak> peaks)
    {
        Signal.Clear();
        foreach (var peak in peaks)
        {
            Signal.Add(peak);
        }

        OnPropertyChanged(nameof(HasSignal));
    }

    /// <summary>Replaces the code terminal content. An empty list hides the terminal.</summary>
    public void SetCode(string header, IReadOnlyList<DiffFileTab> files, IReadOnlyList<DiffLine> lines)
    {
        CodeHeader = header;
        CodeFiles.Clear();
        foreach (var file in files)
        {
            CodeFiles.Add(file);
        }

        CodeLines.Clear();
        foreach (var line in lines)
        {
            CodeLines.Add(line);
        }

        OnPropertyChanged(nameof(HasCode));
        OnPropertyChanged(nameof(HasFileTabs));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
