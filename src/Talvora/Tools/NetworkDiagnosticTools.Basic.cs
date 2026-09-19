using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public static partial class NetworkDiagnosticTools
{
[McpServerTool(
        Name = "talvora_network_interfaces",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraNetworkInterfacesResponse)),
     Description("Return structured local network-interface information including addresses, gateways, DNS servers, DHCP servers, status, type, and link speed.")]
    public static TalvoraNetworkInterfacesResponse NetworkInterfaces()
    {
        var entries = new List<TalvoraNetworkInterfaceEntry>();

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces()
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var properties = networkInterface.GetIPProperties();

                entries.Add(new TalvoraNetworkInterfaceEntry(
                    networkInterface.Id,
                    networkInterface.Name,
                    networkInterface.Description,
                    networkInterface.NetworkInterfaceType.ToString(),
                    networkInterface.OperationalStatus.ToString(),
                    networkInterface.Speed,
                    string.IsNullOrWhiteSpace(properties.DnsSuffix) ? null : properties.DnsSuffix,
                    properties.UnicastAddresses
                        .Select(address => address.Address.ToString())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    properties.GatewayAddresses
                        .Select(gateway => gateway.Address.ToString())
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    properties.DnsAddresses
                        .Select(address => address.ToString())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    properties.DhcpServerAddresses
                        .Select(address => address.ToString())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()));
            }
            catch (NetworkInformationException)
            {
                entries.Add(new TalvoraNetworkInterfaceEntry(
                    networkInterface.Id,
                    networkInterface.Name,
                    networkInterface.Description,
                    networkInterface.NetworkInterfaceType.ToString(),
                    networkInterface.OperationalStatus.ToString(),
                    networkInterface.Speed,
                    null,
                    [],
                    [],
                    [],
                    []));
            }
        }

        return new TalvoraNetworkInterfacesResponse(entries.Count, entries);
    }

    [McpServerTool(
        Name = "talvora_dns_lookup",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraDnsLookupResponse)),
     Description("Resolve any host name or IP address using the Windows/.NET DNS stack and return canonical host name, aliases, and addresses.")]
    public static async Task<TalvoraDnsLookupResponse> DnsLookup(
        string host,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("host is required.", nameof(host));
        }

        var result = await Dns.GetHostEntryAsync(host, cancellationToken);

        return new TalvoraDnsLookupResponse(
            host,
            result.HostName,
            result.Aliases,
            result.AddressList.Select(address => address.ToString()).ToArray());
    }

    [McpServerTool(
        Name = "talvora_ping",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraPingResponse)),
     Description("Send an ICMP echo request to any host with caller-controlled timeout, TTL, Don't Fragment flag, and payload size.")]
    public static async Task<TalvoraPingResponse> PingHost(
        string host,
        int timeoutMilliseconds = 4000,
        int ttl = 128,
        bool dontFragment = false,
        int payloadBytes = 32,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("host is required.", nameof(host));
        }
        if (timeoutMilliseconds < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        }
        if (ttl is < 1 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl));
        }
        if (payloadBytes is < 0 or > 65500)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadBytes));
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var ping = new Ping();
        var buffer = new byte[payloadBytes];
        var options = new PingOptions(ttl, dontFragment);
        var reply = await ping.SendPingAsync(host, timeoutMilliseconds, buffer, options)
            .WaitAsync(cancellationToken);

        return new TalvoraPingResponse(
            host,
            reply.Status.ToString(),
            reply.Address?.ToString(),
            reply.RoundtripTime,
            reply.Buffer?.Length ?? 0,
            reply.Options?.Ttl,
            reply.Options?.DontFragment);
    }
}
