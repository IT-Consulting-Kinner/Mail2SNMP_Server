using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Configuration;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Worker.Services;

/// <summary>
/// Wave D (4): Auto-acknowledge background service.
/// When <see cref="EventSettings.AutoAcknowledgeAfterMinutes"/> is greater than zero, this
/// service scans every minute for <see cref="EventState.New"/> events older than that age
/// and acknowledges them with the System actor. The acknowledge call goes through the
/// regular <see cref="IEventService.AcknowledgeAsync"/> path so the EventConfirmed
/// pair-trap is sent automatically. Useful for self-clearing alarms.
/// </summary>
public class AutoAcknowledgeService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutoAcknowledgeService> _logger;
    private readonly EventSettings _eventSettings;
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);

    public AutoAcknowledgeService(
        IServiceScopeFactory scopeFactory,
        ILogger<AutoAcknowledgeService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _eventSettings = configuration.GetSection("Events").Get<EventSettings>() ?? new EventSettings();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_eventSettings.AutoAcknowledgeAfterMinutes <= 0)
        {
            _logger.LogInformation("AutoAcknowledgeService disabled (Events:AutoAcknowledgeAfterMinutes <= 0)");
            return;
        }

        _logger.LogInformation(
            "AutoAcknowledgeService starting (auto-ack after {Minutes}m, scan every {Interval}s)",
            _eventSettings.AutoAcknowledgeAfterMinutes, ScanInterval.TotalSeconds);

        // Wait briefly so the rest of the host finishes startup before we touch the DB
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanAndAcknowledgeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during auto-acknowledge scan");
            }

            try { await Task.Delay(ScanInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("AutoAcknowledgeService stopped");
    }

    private async Task ScanAndAcknowledgeAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-_eventSettings.AutoAcknowledgeAfterMinutes);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Mail2SnmpDbContext>();
        var eventService = scope.ServiceProvider.GetRequiredService<IEventService>();

        // Cap each scan to a sane batch size so a backlog cannot block the loop forever.
        var dueIds = await db.Events
            .AsNoTracking()
            .Where(e => e.State == EventState.New && e.CreatedUtc <= cutoff)
            .OrderBy(e => e.Id)
            .Take(100)
            .Select(e => e.Id)
            .ToListAsync(ct);

        if (dueIds.Count == 0) return;

        _logger.LogInformation("Auto-acknowledging {Count} events older than {Minutes} minutes",
            dueIds.Count, _eventSettings.AutoAcknowledgeAfterMinutes);

        foreach (var id in dueIds)
        {
            try
            {
                await eventService.AcknowledgeAsync(id, "System.AutoAck", ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auto-acknowledge failed for event {EventId}", id);
            }
        }
    }
}
