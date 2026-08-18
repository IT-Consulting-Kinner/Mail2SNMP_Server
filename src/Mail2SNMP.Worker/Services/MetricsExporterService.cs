using Mail2SNMP.Models.Configuration;
using Microsoft.Extensions.Options;
using Prometheus;

namespace Mail2SNMP.Worker.Services;

/// <summary>
/// Serves the Prometheus scrape endpoint for the Worker process.
/// </summary>
/// <remarks>
/// <para>
/// The Api and Web hosts map <c>/metrics</c> onto the HTTP server they already run. The
/// Worker has no web server, so it had no way to be scraped — and the Worker is where
/// nearly every metric is maintained: mail processed and matched, events created, IMAP
/// connection errors, retention deletions, dead-letter counters, and the mailboxes-in-error
/// gauge that says ingestion has stopped.
/// </para>
/// <para>
/// In All-in-One mode that was hidden, because the Worker services run inside the Web
/// process and share its registry. In a split deployment — the topology the documentation
/// recommends for clustered and HA setups — those metrics were written into a process
/// nobody could scrape, so an alert on them could never fire. This closes that gap.
/// </para>
/// </remarks>
public class MetricsExporterService : IHostedService
{
    private readonly MetricsSettings _settings;
    private readonly ILogger<MetricsExporterService> _logger;
    private IMetricServer? _server;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricsExporterService"/> class.
    /// </summary>
    /// <param name="settings">Validated <c>Metrics</c> options: whether to expose the endpoint, and where.</param>
    /// <param name="logger">Logger for startup and bind diagnostics.</param>
    public MetricsExporterService(IOptions<MetricsSettings> settings, ILogger<MetricsExporterService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Metrics endpoint disabled (Metrics:Enabled=false).");
            return Task.CompletedTask;
        }

        // Force the static metric definitions to register now, so a scrape taken before the
        // first mail arrives reports zeros rather than omitting the series entirely. An
        // alert rule on an absent series behaves differently from one on a zero.
        Infrastructure.Services.Mail2SnmpMetrics.Touch();

        try
        {
            _server = new MetricServer(_settings.Hostname, _settings.Port);
            _server.Start();
            _logger.LogInformation(
                "Metrics endpoint listening on http://{Host}:{Port}/metrics",
                _settings.Hostname, _settings.Port);
        }
        catch (Exception ex)
        {
            // A metrics endpoint is observability, not function. Binding a wildcard prefix
            // without a URL ACL fails with AccessDenied, and refusing to poll mail because
            // of that would be the wrong trade — log loudly and carry on.
            _logger.LogError(ex,
                "Could not start the metrics endpoint on {Host}:{Port}. The Worker continues without it. " +
                "Binding a non-loopback hostname needs administrative rights or a URL ACL " +
                "(netsh http add urlacl url=http://{Host}:{Port}/metrics/ user=<account>).",
                _settings.Hostname, _settings.Port);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_server is null) return;
        try
        {
            await _server.StopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Metrics endpoint did not shut down cleanly.");
        }
    }
}
