using System.IO.Pipes;
using System.Security.Principal;

namespace Talvora.Ipc.Client;

internal static class BrokerServerIdentityValidator
{
    public static void Validate(NamedPipeClientStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var security = stream.GetAccessControl();
        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier
            ?? throw new UnauthorizedAccessException("Talvora Elevated Broker pipe owner SID could not be resolved.");

        using var identity = WindowsIdentity.GetCurrent();
        var currentUser = identity.User
            ?? throw new UnauthorizedAccessException("Current Windows user SID could not be resolved.");
        var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, domainSid: null);

        if (!owner.Equals(currentUser) && !owner.Equals(localSystem))
        {
            throw new UnauthorizedAccessException($"Unexpected Elevated Broker pipe owner: {owner.Value}.");
        }
    }
}
