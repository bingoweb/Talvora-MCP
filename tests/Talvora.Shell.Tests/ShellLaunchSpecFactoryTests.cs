using Talvora.Modules.Shell;

namespace Talvora.Shell.Tests;

[TestClass]
public sealed class ShellLaunchSpecFactoryTests
{
    private static readonly string[] ExpectedPowerShellArguments =
        ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "Get-ChildItem"];

    private static readonly string[] ExpectedCmdArguments = ["/d", "/s", "/c", "dir"];

    [TestMethod]
    public void CreateUsesPwshWithNoninteractiveDefaults()
    {
        var request = new ShellExecutionRequest("Get-ChildItem", ShellKind.PowerShell);

        var spec = ShellLaunchSpecFactory.Create(request);

        Assert.AreEqual("pwsh.exe", spec.FileName);
        CollectionAssert.AreEqual(ExpectedPowerShellArguments, spec.Arguments.ToArray());
    }

    [TestMethod]
    public void CreateCanLoadTheUsersPowershellProfileWhenRequested()
    {
        var request = new ShellExecutionRequest(
            "Write-Output ok",
            ShellKind.PowerShell,
            LoadProfile: true);

        var spec = ShellLaunchSpecFactory.Create(request);

        CollectionAssert.DoesNotContain(spec.Arguments.ToArray(), "-NoProfile");
    }

    [TestMethod]
    public void CreateUsesCmdWithoutShellExecute()
    {
        var request = new ShellExecutionRequest("dir", ShellKind.Cmd);

        var spec = ShellLaunchSpecFactory.Create(request);

        Assert.AreEqual("cmd.exe", spec.FileName);
        CollectionAssert.AreEqual(ExpectedCmdArguments, spec.Arguments.ToArray());
    }
}
