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
        Name = "talvora_tls_inspect",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraTlsInspectResponse)),
     Description("Perform a TLS client handshake to any host:port and return negotiated protocol/cipher/ALPN plus remote certificate and chain details. allowInvalidCertificate=true keeps the diagnostic handshake going while still reporting policy errors.")]
    public static async Task<TalvoraTlsInspectResponse> TlsInspect(
        string host,
        int port = 443,
        string? serverName = null,
        string[]? applicationProtocols = null,
        bool allowInvalidCertificate = false,
        int timeoutSeconds = 30,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("host is required.", nameof(host));
        }
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }
        if (timeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        var effectiveServerName = string.IsNullOrWhiteSpace(serverName)
            ? host
            : serverName;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeoutSeconds > 0)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        }

        SslPolicyErrors policyErrors = SslPolicyErrors.None;

        using var client = new TcpClient();
        var stopwatch = Stopwatch.StartNew();
        await client.ConnectAsync(host, port, timeout.Token);

        await using var network = client.GetStream();
        using var ssl = new SslStream(
            network,
            leaveInnerStreamOpen: false,
            (_, _, _, errors) =>
            {
                policyErrors = errors;
                return allowInvalidCertificate || errors == SslPolicyErrors.None;
            });

        var authenticationOptions = new SslClientAuthenticationOptions
        {
            TargetHost = effectiveServerName,
            EnabledSslProtocols = SslProtocols.None,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        };

        if (applicationProtocols is { Length: > 0 })
        {
            authenticationOptions.ApplicationProtocols = applicationProtocols
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => new SslApplicationProtocol(value))
                .ToList();
        }

        await ssl.AuthenticateAsClientAsync(authenticationOptions, timeout.Token);
        stopwatch.Stop();

        if (ssl.RemoteCertificate is null)
        {
            throw new AuthenticationException("TLS peer did not present a certificate.");
        }

        var certificateBytes = ssl.RemoteCertificate.Export(X509ContentType.Cert);
        using var certificate = X509CertificateLoader.LoadCertificate(certificateBytes);

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        var chainValid = chain.Build(certificate);

        var chainElements = chain.ChainElements
            .Cast<X509ChainElement>()
            .Select(element => new TalvoraTlsChainElement(
                element.Certificate.Subject,
                element.Certificate.Issuer,
                element.Certificate.Thumbprint ?? string.Empty,
                string.Join(
                    "; ",
                    element.ChainElementStatus.Select(status =>
                        $"{status.Status}: {status.StatusInformation.Trim()}"))))
            .ToArray();

        var publicKeySize = 0;
        try
        {
            using var key = certificate.GetRSAPublicKey();
            publicKeySize = key?.KeySize ?? 0;
        }
        catch
        {
            try
            {
                using var key = certificate.GetECDsaPublicKey();
                publicKeySize = key?.KeySize ?? 0;
            }
            catch
            {
            }
        }

        var certInfo = new TalvoraTlsCertificateInfo(
            certificate.Subject,
            certificate.Issuer,
            certificate.Thumbprint ?? string.Empty,
            certificate.SerialNumber,
            certificate.NotBefore,
            certificate.NotAfter,
            certificate.GetNameInfo(X509NameType.DnsName, forIssuer: false),
            certificate.SignatureAlgorithm?.FriendlyName ?? certificate.SignatureAlgorithm?.Value ?? string.Empty,
            certificate.PublicKey.Oid?.FriendlyName ?? certificate.PublicKey.Oid?.Value ?? string.Empty,
            publicKeySize);

        var alpn = ssl.NegotiatedApplicationProtocol.Protocol.IsEmpty
            ? null
            : Encoding.ASCII.GetString(ssl.NegotiatedApplicationProtocol.Protocol.Span);

        return new TalvoraTlsInspectResponse(
            host,
            port,
            effectiveServerName,
            ssl.SslProtocol.ToString(),
            ssl.NegotiatedCipherSuite.ToString(),
            alpn,
            policyErrors.ToString(),
            chainValid,
            chainElements,
            certInfo,
            stopwatch.ElapsedMilliseconds);
    }
}
