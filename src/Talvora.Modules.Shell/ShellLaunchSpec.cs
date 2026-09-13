namespace Talvora.Modules.Shell;

public sealed record ShellLaunchSpec(string FileName, IReadOnlyList<string> Arguments);
