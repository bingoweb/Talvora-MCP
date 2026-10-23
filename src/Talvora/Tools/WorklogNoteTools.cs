using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public sealed record TalvoraWorklogNoteResponse(bool Accepted, string Note);

[McpServerToolType]
public static class WorklogNoteTools
{
    [McpServerTool(
        Name = "talvora_worklog_note",
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraWorklogNoteResponse)),
     Description("Set the one-line task shown on the user's desktop worklog card for this project, in plain Turkish (example: \"D12 — git pull oturum kullanıcısıyla çalışsın\"). Call once when you start a task; it stays until replaced or the work session ends. Never put secrets or command lines in it.")]
    public static TalvoraWorklogNoteResponse SetNote(
        [Description("Turkish task line, at most 140 characters, one line.")] string text,
        [Description("Workspace the task belongs to; the card of that project shows the note.")] string? workspaceRoot = null)
    {
        // The desktop notifier applies the note from the call arguments (it knows the caller's surface).
        var note = WorklogNote.Normalize(text)
            ?? throw new ArgumentException("Görev satırı boş olamaz.", nameof(text));
        return new TalvoraWorklogNoteResponse(true, note);
    }
}
