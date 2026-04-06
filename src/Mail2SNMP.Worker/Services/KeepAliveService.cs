using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mail2SNMP.Worker.Services;

/// <summary>
/// Background service that periodically sends mail2SNMPKeepAliveNotification traps
/// to all SNMP targets that have SendKeepAlive enabled.
/// </summary>
public class KeepAliveService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KeepAliveSettings _settings;
    private readonly ILogger<KeepAliveService> _logger;

    public KeepAliveService(
        IServiceScopeFactory scopeFactory,
        IOptions<KeepAliveSettings> settings,
        ILogger<KeepAliveService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("KeepAliveService disabled via configuration.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _settings.IntervalMinutes));
        _logger.LogInformation("KeepAliveService started (interval: {Minutes} min)", _settings.IntervalMinutes);

        // Wait one interval before sending the first KeepAlive (Service-Start ist genug)
        try
        {
            await Task.Delay(interval, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var channels = scope.ServiceProvider.GetServices<INotificationChannel>();
                var snmp = channels.FirstOrDefault(c => c.ChannelName == "snmp");
                if (snmp != null)
                {
                    await snmp.SendKeepAliveAsync(stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "KeepAlive iteration failed");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }
}
