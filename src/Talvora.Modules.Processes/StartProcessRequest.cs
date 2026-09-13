namespace Talvora.Modules.Processes;

public sealed record StartProcessRequest(
    string FileName,
    IReadOnlyList<string>? Arguments = null,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string?>? Environment = null,
    bool CreateNoWindow = false);
