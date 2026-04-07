using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Configuration;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mail2SNMP.Worker.Services;

/// <summary>
/// Background service that periodically checks the update feed and emits an SNMP
/// mail2SNMPUpdateNotification trap when a newer version is available.
/// </summary>
public class UpdateCheckService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly UpdateCheckSettings _settings;
    private readonly ILogger<UpdateCheckService> _logger;

    public UpdateCheckService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        IOptions<UpdateCheckSettings> settings,
        ILogger<UpdateCheckService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("UpdateCheckService disabled via configuration.");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, _settings.IntervalHours));
        _logger.LogInformation("UpdateCheckService started (interval: {Hours} h, mode: {Mode})", _settings.IntervalHours, _settings.TrapMode);

        // N10: only the elected primary instance performs the update check.
        // Without this guard a 4-node cluster would emit 4 identical "update
        // available" SNMP traps every interval, multiplying the alert volume
        // operators see. Single-instance deployments are unaffected — the
        // single node is implicitly the primary.
        var instanceId = $"{Environment.MachineName}-{Environment.ProcessId}";

        // Run immediately at startup, then every interval.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                bool isPrimary;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var leaseService = scope.ServiceProvider.GetRequiredService<IWorkerLeaseService>();
                    isPrimary = await PrimaryElection.IsPrimaryAsync(leaseService, instanceId, stoppingToken);
                }

                if (isPrimary)
                {
                    await CheckOnceAsync(stoppingToken);
                }
                else
                {
                    _logger.LogDebug("UpdateCheckService: not primary, skipping check");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Update check iteration failed");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Performs a single update-feed check, persists the latest version info, and
    /// emits an SNMP Update notification trap when applicable based on the configured TrapMode.
    /// </summary>
    private async Task CheckOnceAsync(CancellationToken ct)
    {
        var current = GetCurrentVersion();
        UpdateFeedResponse? feed;
        try
        {
            var http = _httpClientFactory.CreateClient("UpdateCheck");
            var json = await http.GetStringAsync(_settings.Url, ct);
            feed = JsonSerializer.Deserialize<UpdateFeedResponse>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch update feed from {Url}", _settings.Url);
            return;
        }

        if (feed?.Version == null)
        {
            _logger.LogWarning("Update feed at {Url} returned no version", _settings.Url);
            return;
        }

        if (!IsNewer(feed.Version, current))
        {
            _logger.LogDebug("No update available (current: {Current}, feed: {Feed})", current, feed.Version);
            return;
        }

        _logger.LogInformation("Update available: {Current} → {Available} ({Date})", current, feed.Version, feed.PublishDate);

        var info = new UpdateInfo
        {
            CurrentVersion   = current,
            AvailableVersion = feed.Version,
            DownloadUrl      = feed.DownloadLink ?? string.Empty,
            PublishDate      = feed.PublishDate ?? string.Empty
        };

        // Persist info for the UI to display, regardless of trap mode
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Mail2SnmpDbContext>();

        await UpsertSettingAsync(db, "update.available_version", feed.Version, ct);
        await UpsertSettingAsync(db, "update.publish_date",      feed.PublishDate ?? "", ct);
        await UpsertSettingAsync(db, "update.download_link",     feed.DownloadLink ?? "", ct);
        await db.SaveChangesAsync(ct);

        // Decide whether to send a trap based on TrapMode.
        // T9: validate TrapMode against the known values; warn once per check if it
        // is not recognised so a typo in appsettings.json is visible in the log.
        var mode = (_settings.TrapMode ?? "UntilUpdated").Trim();
        if (!mode.Equals("Off", StringComparison.OrdinalIgnoreCase) &&
            !mode.Equals("Once", StringComparison.OrdinalIgnoreCase) &&
            !mode.Equals("UntilUpdated", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "UpdateCheck:TrapMode value '{Mode}' is not recognised. " +
                "Expected one of: Off, Once, UntilUpdated. Falling back to 'UntilUpdated'.",
                mode);
            mode = "UntilUpdated";
        }

        if (mode.Equals("Off", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (mode.Equals("Once", StringComparison.OrdinalIgnoreCase))
        {
            var lastNotified = await db.Settings.FindAsync(new object[] { Setting.LastNotifiedUpdateVersion }, ct);
            if (lastNotified?.Value == feed.Version)
            {
                _logger.LogDebug("Update trap already sent for version {Version} (Once mode)", feed.Version);
                return;
            }
        }
        // "UntilUpdated" → send every check; we never reach here when current >= feed.Version anyway

        var snmp = scope.ServiceProvider.GetServices<INotificationChannel>()
            .FirstOrDefault(c => c.ChannelName == "snmp");
        if (snmp != null)
        {
            await snmp.SendUpdateAvailableAsync(info, ct);
            await UpsertSettingAsync(db, Setting.LastNotifiedUpdateVersion, feed.Version, ct);
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task UpsertSettingAsync(Mail2SnmpDbContext db, string key, string value, CancellationToken ct)
    {
        var existing = await db.Settings.FindAsync(new object[] { key }, ct);
        if (existing == null)
        {
            db.Settings.Add(new Setting { Key = key, Value = value, UpdatedUtc = DateTime.UtcNow });
        }
        else
        {
            existing.Value = value;
            existing.UpdatedUtc = DateTime.UtcNow;
        }
    }

    private static string GetCurrentVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(UpdateCheckService).Assembly;
        var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(infoVer))
        {
            // Strip git hash suffix if present (e.g. "1.0.0+abcdef")
            var plus = infoVer.IndexOf('+');
            return plus > 0 ? infoVer[..plus] : infoVer;
        }
        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private static bool IsNewer(string available, string current)
    {
        if (Version.TryParse(Pad(available), out var av) && Version.TryParse(Pad(current), out var cv))
            return av > cv;
        return !string.Equals(available, current, StringComparison.OrdinalIgnoreCase);
    }

    private static string Pad(string v)
    {
        var parts = v.Split('.');
        while (parts.Length < 3)
            parts = parts.Append("0").ToArray();
        return string.Join('.', parts.Take(4));
    }

    private class UpdateFeedResponse
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("publish_date")]
        public string? PublishDate { get; set; }

        [JsonPropertyName("download_link")]
        public string? DownloadLink { get; set; }
    }
}
