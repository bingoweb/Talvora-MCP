namespace Talvora;

internal sealed partial class TalvoraDesktopProgressNotifier
{
    private static bool IsCanonicalPackageBuildScript(string script) =>
        script.Contains(
            "Build-Windows-Installer.ps1",
            StringComparison.OrdinalIgnoreCase);
}
