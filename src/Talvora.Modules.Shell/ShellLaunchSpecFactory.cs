namespace Talvora.Modules.Shell;

public static class ShellLaunchSpecFactory
{
    public static ShellLaunchSpec Create(ShellExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Command);

        return request.Shell switch
        {
            ShellKind.PowerShell => CreatePowerShell(request),
            ShellKind.Cmd => new ShellLaunchSpec("cmd.exe", ["/d", "/s", "/c", request.Command]),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Shell, "Unsupported shell."),
        };
    }

    private static ShellLaunchSpec CreatePowerShell(ShellExecutionRequest request)
    {
        var arguments = new List<string> { "-NoLogo" };
        if (!request.LoadProfile)
        {
            arguments.Add("-NoProfile");
        }

        arguments.Add("-NonInteractive");
        arguments.Add("-Command");
        arguments.Add(request.Command);

        return new ShellLaunchSpec("pwsh.exe", arguments);
    }
}
