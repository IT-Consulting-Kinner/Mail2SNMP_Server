using System.Net;
using System.Net.Sockets;

namespace Mail2SNMP.Infrastructure.Security;

/// <summary>
/// R1: Server-Side Request Forgery (SSRF) guard. Refuses outbound HTTP requests
/// that target loopback, link-local (incl. cloud metadata service 169.254.169.254),
/// or RFC 1918 private network addresses. Without this check an authenticated
/// Operator could configure a webhook target pointing at
/// <c>http://169.254.169.254/latest/meta-data/iam/security-credentials/</c> and
/// have the Mail2SNMP host (which lives inside the cloud VPC) leak its
/// instance-profile credentials on every event.
///
/// The default policy is "deny private and metadata-style addresses". Operators
/// who legitimately need internal webhook targets (e.g. an on-prem Splunk that
/// happens to be at 10.0.0.5) can opt out per appsettings.json:
/// <code>
/// "Security": { "AllowPrivateWebhookTargets": true }
/// </code>
/// </summary>
public static class SsrfGuard
{
    /// <summary>
    /// Returns <c>true</c> if the URL is safe to fetch from a server-side
    /// HttpClient. Rejects malformed URLs, non-HTTP(S) schemes, loopback,
    /// link-local, ULA, and RFC 1918 addresses unless <paramref name="allowPrivate"/>
    /// is true.
    /// </summary>
    public static bool IsSafeOutboundUrl(string? url, bool allowPrivate, out string? rejectionReason)
    {
        rejectionReason = null;

        if (string.IsNullOrWhiteSpace(url))
        {
            rejectionReason = "URL is empty.";
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            rejectionReason = "URL is not a well-formed absolute URI.";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            rejectionReason = $"Only http and https schemes are allowed (got '{uri.Scheme}').";
            return false;
        }

        // Always block the obvious metadata host name even if allowPrivate is true.
        if (uri.Host.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase))
        {
            rejectionReason = "GCP metadata service is blocked.";
            return false;
        }

        // Resolve the host to all IPs and reject if ANY of them is private.
        // We deliberately resolve here so an attacker cannot bypass with a
        // hostname that points to 169.254.169.254 — DNS rebinding mitigation.
        IPAddress[] addresses;
        try
        {
            addresses = Dns.GetHostAddresses(uri.Host);
        }
        catch
        {
            // DNS lookup failure: refuse rather than risk a follow-up resolution
            // bypass. Operators who need broken DNS targets can use a static
            // hosts file entry on the worker host.
            rejectionReason = $"DNS resolution for '{uri.Host}' failed.";
            return false;
        }

        if (addresses.Length == 0)
        {
            rejectionReason = $"Host '{uri.Host}' resolved to no addresses.";
            return false;
        }

        foreach (var ip in addresses)
        {
            if (IsPrivate(ip))
            {
                if (!allowPrivate)
                {
                    rejectionReason =
                        $"Host '{uri.Host}' resolves to a private/loopback/link-local address ({ip}). " +
                        "Set Security:AllowPrivateWebhookTargets=true in appsettings.json to allow this.";
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Returns <c>true</c> when the IP belongs to any address range that should
    /// not be reachable from a server-side HTTP client by default.
    /// </summary>
    private static bool IsPrivate(IPAddress ip)
    {
        // S3: unwrap IPv4-mapped IPv6 (::ffff:a.b.c.d) so an attacker cannot
        // bypass the IPv4 byte checks below by encoding 169.254.169.254 as
        // ::ffff:169.254.169.254. IsLoopback() catches the loopback case but
        // not arbitrary mapped addresses.
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (IPAddress.IsLoopback(ip)) return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            // 10.0.0.0/8
            if (b[0] == 10) return true;
            // 172.16.0.0/12
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            // 192.168.0.0/16
            if (b[0] == 192 && b[1] == 168) return true;
            // 169.254.0.0/16 — link-local incl. cloud metadata 169.254.169.254
            if (b[0] == 169 && b[1] == 254) return true;
            // 0.0.0.0/8 — "this network", typically resolves on listen sockets
            if (b[0] == 0) return true;
            // 100.64.0.0/10 — carrier-grade NAT, often used for internal infra
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true;
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // IsIPv6LinkLocal covers fe80::/10
            if (ip.IsIPv6LinkLocal) return true;
            // IsIPv6SiteLocal covers fec0::/10 (deprecated but defensive)
            if (ip.IsIPv6SiteLocal) return true;
            // ULA fc00::/7
            var b = ip.GetAddressBytes();
            if ((b[0] & 0xfe) == 0xfc) return true;
        }

        return false;
    }
}
