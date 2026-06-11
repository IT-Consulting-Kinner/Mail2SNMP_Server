using Mail2SNMP.Infrastructure.Security;

namespace Mail2SNMP.Tests.Infrastructure;

/// <summary>
/// Peer-review: the SSRF guard is a security-critical control that was previously
/// untested. These tests pin its decision table.
///
/// Every case uses an IP-literal host (or a scheme/format rejection that happens
/// before resolution), so <see cref="System.Net.Dns.GetHostAddresses(string)"/>
/// returns the literal without a real DNS query — the tests are deterministic and
/// run offline with no network dependency.
/// </summary>
public class SsrfGuardTests
{
    [Theory]
    // Loopback
    [InlineData("http://127.0.0.1/")]
    [InlineData("https://127.0.0.1:8443/admin")]
    // Cloud metadata service (the canonical SSRF target)
    [InlineData("http://169.254.169.254/latest/meta-data/iam/security-credentials/")]
    // RFC 1918 private ranges
    [InlineData("http://10.0.0.5/webhook")]
    [InlineData("http://172.16.5.5/")]
    [InlineData("http://172.31.255.255/")]
    [InlineData("http://192.168.1.1/")]
    // 0.0.0.0/8 "this network"
    [InlineData("http://0.0.0.0/")]
    // Carrier-grade NAT 100.64.0.0/10
    [InlineData("http://100.64.0.1/")]
    public void IsSafeOutboundUrl_PrivateAndMetadata_Blocked_WhenNotAllowed(string url)
    {
        var safe = SsrfGuard.IsSafeOutboundUrl(url, allowPrivate: false, out var reason);
        Assert.False(safe);
        Assert.NotNull(reason);
    }

    [Theory]
    [InlineData("http://10.0.0.5/webhook")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://127.0.0.1/")]
    public void IsSafeOutboundUrl_Private_Allowed_WhenOptedIn(string url)
    {
        // Operators with internal targets (e.g. on-prem Splunk) can opt in.
        var safe = SsrfGuard.IsSafeOutboundUrl(url, allowPrivate: true, out var reason);
        Assert.True(safe);
        Assert.Null(reason);
    }

    [Theory]
    [InlineData("http://8.8.8.8/")]
    [InlineData("https://1.1.1.1/webhook")]
    public void IsSafeOutboundUrl_PublicIp_Allowed(string url)
    {
        // Public, routable IP literal — resolves to itself offline and must pass.
        var safe = SsrfGuard.IsSafeOutboundUrl(url, allowPrivate: false, out var reason);
        Assert.True(safe);
        Assert.Null(reason);
    }

    [Theory]
    [InlineData("ftp://example.com/file")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://10.0.0.1/")]
    public void IsSafeOutboundUrl_NonHttpScheme_Blocked(string url)
    {
        var safe = SsrfGuard.IsSafeOutboundUrl(url, allowPrivate: true, out var reason);
        Assert.False(safe);
        Assert.Contains("scheme", reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not-a-url")]
    [InlineData("/relative/path")]
    public void IsSafeOutboundUrl_EmptyOrMalformed_Blocked(string? url)
    {
        var safe = SsrfGuard.IsSafeOutboundUrl(url, allowPrivate: true, out var reason);
        Assert.False(safe);
        Assert.NotNull(reason);
    }

    [Fact]
    public void IsSafeOutboundUrl_GcpMetadataHost_Blocked_EvenWhenPrivateAllowed()
    {
        // The GCP metadata hostname is blocked unconditionally — even allowPrivate
        // must not open this door, and the check fires before DNS resolution.
        var safe = SsrfGuard.IsSafeOutboundUrl(
            "http://metadata.google.internal/computeMetadata/v1/", allowPrivate: true, out var reason);
        Assert.False(safe);
        Assert.Contains("metadata", reason, StringComparison.OrdinalIgnoreCase);
    }
}
