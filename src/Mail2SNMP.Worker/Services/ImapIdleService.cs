using System.Threading.Channels;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Configuration;
using Mail2SNMP.Worker.Models;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Worker.Services;

/// <summary>
/// G8: IMAP IDLE service. When <c>Imap:UseIdle = true</c> is configured, this service
/// holds a persistent IDLE connection per active mailbox. On every CountChanged
/// notification it enqueues a MailWorkItem for each Job that uses that mailbox so the
/// existing <see cref="MailPollingService"/> consumer drains the new mail immediately —
/// effectively turning fixed-interval polling into push-based ingestion. Polling stays
/// configured as a safety net (handles gaps where the IDLE connection is being
/// re-established).
/// </summary>
public class ImapIdleService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Channel<MailWorkItem> _channel;
    private readonly ILogger<ImapIdleService> _logger;
    private readonly ImapSettings _imapSettings;
    private readonly bool _enabled;
    private static readonly TimeSpan IdleRefreshInterval = TimeSpan.FromMinutes(25); // RFC 2177: <29min
    private static readonly TimeSpan ReconnectBackoff = TimeSpan.FromSeconds(15);

    public ImapIdleService(
        IServiceScopeFactory scopeFactory,
        Channel<MailWorkItem> channel,
        IConfiguration configuration,
        ILogger<ImapIdleService> logger)
    {
        _scopeFactory = scopeFactory;
        _channel = channel;
        _logger = logger;
        _imapSettings = configuration.GetSection("Imap").Get<ImapSettings>() ?? new ImapSettings();
        _enabled = configuration.GetValue<bool>("Imap:UseIdle");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("ImapIdleService disabled (Imap:UseIdle=false)");
            return;
        }

        _logger.LogInformation("ImapIdleService starting — push-based mail ingestion");

        // Wait for the rest of the host to come up
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
        catch (OperationCanceledException) { return; }

        // Per-mailbox loops, restarted on error.
        var loops = new Dictionary<int, Task>();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                List<int> activeMailboxIds;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<Mail2SnmpDbContext>();
                    activeMailboxIds = await db.Mailboxes
                        .Where(m => m.IsActive)
                        .Select(m => m.Id)
                        .ToListAsync(stoppingToken);
                }

                // Start a loop for any newly active mailbox
                foreach (var id in activeMailboxIds)
                {
                    if (!loops.ContainsKey(id) || loops[id].IsCompleted)
                    {
                        loops[id] = Task.Run(() => RunIdleLoopAsync(id, stoppingToken), stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ImapIdleService supervisor loop");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("ImapIdleService stopped");
    }

    /// <summary>
    /// Per-mailbox loop. Holds an IDLE connection until the cancellation token fires
    /// or an unrecoverable error occurs. On any failure we wait and reconnect.
    /// </summary>
    private async Task RunIdleLoopAsync(int mailboxId, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await OneIdleSessionAsync(mailboxId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IMAP IDLE session for mailbox {MailboxId} failed; reconnecting in {Sec}s",
                    mailboxId, ReconnectBackoff.TotalSeconds);
                try { await Task.Delay(ReconnectBackoff, stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task OneIdleSessionAsync(int mailboxId, CancellationToken stoppingToken)
    {
        // Resolve mailbox + decrypt password in a fresh scope
        Mail2SNMP.Models.Entities.Mailbox? mailbox;
        List<int> jobIdsForMailbox;
        string password;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Mail2SnmpDbContext>();
            mailbox = await db.Mailboxes.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mailboxId, stoppingToken);
            if (mailbox == null || !mailbox.IsActive) return;

            jobIdsForMailbox = await db.Jobs.AsNoTracking()
                .Where(j => j.MailboxId == mailboxId && j.IsActive)
                .Select(j => j.Id)
                .ToListAsync(stoppingToken);

            var enc = scope.ServiceProvider.GetRequiredService<ICredentialEncryptor>();
            try { password = enc.Decrypt(mailbox.EncryptedPassword); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cannot decrypt password for mailbox {Name} in IDLE service", mailbox.Name);
                return;
            }
        }

        if (jobIdsForMailbox.Count == 0) return;

        using var client = new ImapClient();
        var ssl = mailbox.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        connectCts.CancelAfter(TimeSpan.FromSeconds(_imapSettings.ConnectTimeoutSeconds * 3));

        await client.ConnectAsync(mailbox.Host, mailbox.Port, ssl, connectCts.Token);
        await client.AuthenticateAsync(mailbox.Username, password, connectCts.Token);

        if (!client.Capabilities.HasFlag(ImapCapabilities.Idle))
        {
            _logger.LogWarning("IMAP server for mailbox {Name} does not support IDLE — falling back to polling",
                mailbox.Name);
            return;
        }

        var folder = await client.GetFolderAsync(mailbox.Folder, stoppingToken);
        await folder.OpenAsync(FolderAccess.ReadOnly, stoppingToken);

        _logger.LogInformation("IDLE session opened for mailbox {Name} ({Folder})", mailbox.Name, mailbox.Folder);

        // Wire CountChanged → enqueue work item for each job
        folder.CountChanged += (s, e) =>
        {
            try
            {
                foreach (var jobId in jobIdsForMailbox)
                {
                    if (!_channel.Writer.TryWrite(new MailWorkItem(jobId, mailboxId, 0)))
                        _logger.LogWarning("IDLE: channel full, dropping work item for job {JobId}", jobId);
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "IDLE CountChanged handler failed"); }
        };

        // IDLE in 25-minute slices, refreshing the connection so the server doesn't kill it
        while (!stoppingToken.IsCancellationRequested && client.IsConnected)
        {
            using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            idleCts.CancelAfter(IdleRefreshInterval);
            try
            {
                await client.IdleAsync(idleCts.Token, stoppingToken);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // Refresh interval elapsed; loop and re-enter IDLE
            }
        }

        if (client.IsConnected)
        {
            try { await client.DisconnectAsync(true, CancellationToken.None); } catch { }
        }
    }
}
