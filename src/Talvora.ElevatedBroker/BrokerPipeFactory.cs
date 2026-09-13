using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes;

namespace Talvora.ElevatedBroker;

internal static class BrokerPipeFactory
{
    public static NamedPipeServerStream Create(
        CreateNamedPipeServerStreamContext context,
        string allowedUserSid)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(allowedUserSid);

        var allowedUser = new SecurityIdentifier(allowedUserSid);
        using var serverIdentity = WindowsIdentity.GetCurrent();
        var serverUser = serverIdentity.User
            ?? throw new InvalidOperationException("Elevated Broker server SID could not be resolved.");
        var security = CreateSecurityDescriptor(allowedUser, serverUser);

        return NamedPipeServerStreamAcl.Create(
            context.NamedPipeEndPoint.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            context.PipeOptions,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }

    internal static PipeSecurity CreateSecurityDescriptor(
        SecurityIdentifier allowedUser,
        SecurityIdentifier serverUser)
    {
        ArgumentNullException.ThrowIfNull(allowedUser);
        ArgumentNullException.ThrowIfNull(serverUser);

        var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, domainSid: null);
        var security = new PipeSecurity();

        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(serverUser);
        security.AddAccessRule(new PipeAccessRule(
            localSystem,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            serverUser,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            allowedUser,
            PipeAccessRights.ReadWrite | PipeAccessRights.ReadPermissions,
            AccessControlType.Allow));

        return security;
    }

    public static string ResolveAllowedUserSid(string? configuredSid)
    {
        if (!string.IsNullOrWhiteSpace(configuredSid))
        {
            _ = new SecurityIdentifier(configuredSid);
            return configuredSid;
        }

        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value
            ?? throw new InvalidOperationException("Windows user SID could not be resolved for the Elevated Broker pipe ACL.");
    }
}
