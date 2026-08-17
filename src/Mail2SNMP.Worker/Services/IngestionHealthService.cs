using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Mail2SNMP.Worker.Services;

/// <summary>
/// Watches whether mail ingestion is working and pushes a notification when that changes.
/// </summary>
/// <remarks>
/// <para>
/// The product's whole purpose is telling a monitoring system when something is wrong — but
/// it had no way to say that about itself. A mailbox that stops polling raises no event,
/// precisely because no mail reaches it, so a dead ingestion path is indistinguishable from
/// a quiet night. The only signal was <c>MailboxesInError</c> on the dashboard: pull-based,
/// and only if somebody happened to look.
/// </para>
/// <para>
/// This service closes that loop. It emits on <em>transition</em> rather than on every
/// cycle — a trap per minute for the same broken mailbox is noise an operator learns to
/// filter, which is worse than no trap at all — and it emits a recovery notification too,
/// so the NOC can clear the alarm without a human deciding it looks fine now.
/// </para>
/// </remarks>
public class IngestionHealthService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    /// <summary>Grace period before the first check, so a cold start is not reported as an outage.</summary>
    private static readonly TimeSpan StartupGrace = TimeSpan.FromMinutes(3);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IngestionHealthService> _logger;
    private readonly string _instanceId;

    /// <summary>
    /// Last reported state. <c>null</c> until the first evaluation, so the service does not
    /// announce a transition on startup that nothing actually transitioned into.
    /// </summary>
    private bool? _lastDegraded;

    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionHealthService"/> class.
    /// </summary>
    /// <param name="scopeFactory">Factory used to create a scope per check for the database and channels.</param>
    /// <param name="logger">Logger for health transitions and leadership decisions.</param>
    public IngestionHealthService(IServiceScopeFactory scopeFactory, ILogger<IngestionHealthService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        // Mirrors HeartbeatService's format so the lease lookup matches.
        _instanceId = $"{Environment.MachineName}-{Environment.ProcessId}";
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IngestionHealthService started (instance: {Instance})", _instanceId);

        try
        {
            await Task.Delay(StartupGrace, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ingestion health check failed. Will retry in {Interval}.", CheckInterval);
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("IngestionHealthService stopped");
    }

    /// <summary>
    /// Evaluates ingestion health, always updating the metric, and pushes a notification
    /// only when the state has changed since the last check.
    /// </summary>
    internal async Task CheckAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<Mail2SnmpDbContext>();

        var failing = await db.Mailboxes
            .AsNoTracking()
            .Where(m => m.IsActive && m.LastError != null)
            .Select(m => m.Name)
            .OrderBy(n => n)
            .ToListAsync(ct);

        // The gauge is updated on every instance and every cycle: it describes current
        // state, so a stale value would be worse than none. Only the notification is
        // leader-gated and transition-gated.
        Mail2SnmpMetrics.MailboxesInError.Set(failing.Count);

        var degraded = failing.Count > 0;
        if (_lastDegraded == degraded)
            return;

        var first = _lastDegraded is null;
        _lastDegraded = degraded;

        // Nothing to announce on the first healthy evaluation — that is the normal state,
        // not a recovery from anything.
        if (first && !degraded)
            return;

        // Cluster: only the elected primary notifies, or every instance would send the
        // same trap for the same outage. Uses the shared helper the other leader-gated
        // services use, so a change to the election rule cannot apply to some of them.
        var lease = sp.GetRequiredService<IWorkerLeaseService>();
        if (!await PrimaryElection.IsPrimaryAsync(lease, _instanceId, ct))
        {
            _logger.LogDebug("Ingestion health changed (degraded={Degraded}) but this instance is not primary.", degraded);
            return;
        }

        var message = degraded
            ? $"Mail ingestion is degraded: {failing.Count} active mailbox(es) are failing to poll — {string.Join(", ", failing.Take(10))}" +
              (failing.Count > 10 ? $" and {failing.Count - 10} more" : string.Empty) +
              ". Alerts from their jobs are NOT being generated."
            : "Mail ingestion has recovered: all active mailboxes are polling again.";

        if (degraded)
            _logger.LogError("{Message}", message);
        else
            _logger.LogInformation("{Message}", message);

        foreach (var channel in sp.GetRequiredService<IEnumerable<INotificationChannel>>())
        {
            try
            {
                await channel.SendIngestionHealthAsync(degraded, message, ct);
            }
            catch (Exception ex)
            {
                // One broken channel must not stop the others from reporting the outage.
                _logger.LogError(ex, "Channel {Channel} failed to report ingestion health.", channel.ChannelName);
            }
        }
    }
}
