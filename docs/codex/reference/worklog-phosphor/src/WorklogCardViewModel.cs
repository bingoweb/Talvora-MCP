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

public sealed record DiffLine(DiffLineKind Kind, string Number, string Text)
{
    public string Marker => Kind switch
    {
        DiffLineKind.Added => "+",
        DiffLineKind.Removed => "−",
        DiffLineKind.File => "▌",
        _ => " ",
    };
}

/// <summary>
/// Everything the worklog card shows. Filled only from real frames (no fabricated telemetry).
/// </summary>
public sealed class WorklogCardViewModel : INotifyPropertyChanged
{
    private const int VisibleTimelineRows = 5;

    private WorklogVisualState state;
    private string prompt = string.Empty;
    private string actor = string.Empty;
    private string title = string.Empty;
    private string? factLine;
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

    public string Title { get => title; set => Set(ref title, value); }

    public string? FactLine { get => factLine; set => Set(ref factLine, value); }

    public string? OutcomeReason { get => outcomeReason; set => Set(ref outcomeReason, value); }

    public string? OutcomeNextStep { get => outcomeNextStep; set => Set(ref outcomeNextStep, value); }

    /// <summary>"+9 adım daha" or null.</summary>
    public string? MoreSteps { get => moreSteps; set => Set(ref moreSteps, value); }

    /// <summary>"14 adım · 06:12 · 5 dosya değişti" (zero parts omitted by the mapper).</summary>
    public string Footer { get => footer; set => Set(ref footer, value); }

    /// <summary>"diff · 14:02:11" — time of the real code change.</summary>
    public string CodeHeader { get => codeHeader; set => Set(ref codeHeader, value); }

    public bool HasCode => CodeLines.Count > 0;

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
