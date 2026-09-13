using System.Security.Principal;
using Talvora.ElevatedBroker;

namespace Talvora.Ipc.Tests;

[TestClass]
public sealed class BrokerPipeSecurityTests
{
    [TestMethod]
    public void SecurityDescriptorPinsOwnerToBrokerIdentity()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var serverUser = identity.User
            ?? throw new InvalidOperationException("Current Windows user SID could not be resolved for the test.");

        var security = BrokerPipeFactory.CreateSecurityDescriptor(serverUser, serverUser);
        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;

        Assert.IsNotNull(owner);
        Assert.AreEqual(serverUser.Value, owner.Value);
    }
}
