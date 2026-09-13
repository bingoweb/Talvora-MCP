using Talvora.Ipc.Contracts;

namespace Talvora.Ipc.Tests;

[TestClass]
public sealed class BrokerProtocolV2Tests
{
    [TestMethod]
    public void BrokerProtocolAndInstallScriptsUseV2()
    {
        Assert.AreEqual(2, BrokerProtocol.CurrentVersion);
        Assert.AreEqual("Talvora.ElevatedBroker.v2", BrokerProtocol.DefaultPipeName);

        var root = FindRepositoryRoot();
        var installScript = File.ReadAllText(Path.Combine(root, "scripts", "Install-BrokerService.ps1"));
        var smokeScript = File.ReadAllText(Path.Combine(root, "scripts", "Test-BrokerSmoke.ps1"));
        var installedServiceScript = File.ReadAllText(Path.Combine(root, "scripts", "Test-InstalledBrokerService.ps1"));

        StringAssert.Contains(installScript, "PipeName = 'Talvora.ElevatedBroker.v2'");
        StringAssert.Contains(installScript, "pipe:         Talvora.ElevatedBroker.v2");
        StringAssert.Contains(smokeScript, "$brokerHealth.protocolVersion -ne 2");
        StringAssert.Contains(installedServiceScript, "$brokerHealth.protocolVersion -ne 2");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HANDOFF.md")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Talvora repository root could not be located from the test output directory.");
    }
}
