using Mail2SNMP.Infrastructure.Services;
using Prometheus;

namespace Mail2SNMP.Tests.Infrastructure;

/// <summary>
/// Review finding: nearly every metric is maintained by the Worker, which had no scrape
/// endpoint at all. In a split deployment — the topology recommended for clustered and HA
/// setups — those values were written into a process nobody could reach, so an alert on
/// them could never fire. The Worker now serves its own endpoint; these tests cover the
/// subtle half of that fix.
/// </summary>
public class MetricsRegistrationTests
{
    [Fact]
    public void Touch_RegistersTheSeries_SoAScrapeReportsZeroRatherThanNothing()
    {
        Mail2SnmpMetrics.Touch();

        using var buffer = new MemoryStream();
        Metrics.DefaultRegistry.CollectAndExportAsTextAsync(buffer).GetAwaiter().GetResult();
        var scrape = System.Text.Encoding.UTF8.GetString(buffer.ToArray());

        // Static fields initialize on first access to the type. Without an explicit touch a
        // freshly started Worker serves a scrape that OMITS these series, and an alert rule
        // evaluated against an absent series behaves differently from one against a zero —
        // "no mailboxes in error" and "no data" are not the same statement.
        Assert.Contains("mail2snmp_mailboxes_in_error", scrape);
        Assert.Contains("mail2snmp_events_active", scrape);
        Assert.Contains("mail2snmp_imap_active_connections", scrape);
        Assert.Contains("mail2snmp_webhook_deadletter_pending", scrape);
    }

    [Fact]
    public void EveryMetricNameCarriesTheProductPrefix()
    {
        Mail2SnmpMetrics.Touch();

        using var buffer = new MemoryStream();
        Metrics.DefaultRegistry.CollectAndExportAsTextAsync(buffer).GetAwaiter().GetResult();
        var scrape = System.Text.Encoding.UTF8.GetString(buffer.ToArray());

        // A metric that slips out without the prefix is invisible to any dashboard or alert
        // built on `mail2snmp_*`, and nothing else in the build would notice.
        var ours = scrape.Split('\n')
            .Where(l => l.StartsWith("# HELP ", StringComparison.Ordinal))
            .Select(l => l.Split(' ')[2])
            .Where(n => n.Contains("mail2snmp", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(ours);
        Assert.All(ours, n => Assert.StartsWith("mail2snmp_", n, StringComparison.Ordinal));
    }
}
