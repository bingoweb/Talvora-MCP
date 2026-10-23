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

public sealed record WorklogTimelineRow(WorklogRowState State, string Time, string Label)
{
    public string Tag => State switch
    {
        WorklogRowState.Running => "[ ▸ ]",
        WorklogRowState.Succeeded => "[ ✓ ]",
        WorklogRowState.Failed => "[ ✗ ]",
        WorklogRowState.Cancelled => "[ ■ ]",
        _ => "[ · ]",
    };
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

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<WorklogTimelineRow> Timeline { get; } = [];

    public ObservableCollection<DiffFileTab> CodeFiles { get; } = [];

    public ObservableCollection<DiffLine> CodeLines { get; } = [];

    /// <summary>"nerede" rows, at most three; the rest is counted in <see cref="MoreChangedFiles"/>.</summary>
    public ObservableCollection<WorklogChangeRow> ChangeRows { get; } = [];

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
            }
        }
    }

    public string StateLabel => State switch
    {
        WorklogVisualState.Running => "[ÇALIŞIYOR]",
        WorklogVisualState.Succeeded => "[TAMAM]",
        WorklogVisualState.Failed => "[HATA]",
        WorklogVisualState.Cancelled => "[DURDU]",
        _ => "[BEKLİYOR]",
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
    public string CodeHeader { get => codeHeader; set => Set(ref codeHeader, value); }

    public bool HasCode => CodeLines.Count > 0;

    /// <summary>File tabs only help with two or more files; one file is already named by its header row.</summary>
    public bool HasFileTabs => CodeFiles.Count > 1;

    public bool HasTimeline => Timeline.Count > 0;

    public bool AnimationsEnabled { get => animationsEnabled; set => Set(ref animationsEnabled, value); }

    /// <summary>Shows the "ayrıntılar ▸" button. Stays false until card N6 provides the evidence surface.</summary>
    public bool HasDetails { get => hasDetails; set => Set(ref hasDetails, value); }

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
