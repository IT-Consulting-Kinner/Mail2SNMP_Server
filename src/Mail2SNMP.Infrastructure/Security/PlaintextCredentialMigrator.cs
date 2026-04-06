using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Infrastructure.Security;

/// <summary>
/// J1: One-shot startup migration that re-encrypts any credential rows that were
/// stored as plaintext by older builds (before the service-layer encryption funnel
/// existed). For each row in Mailboxes / SnmpTargets / WebhookTargets we attempt to
/// decrypt the credential field — a successful decrypt means it is already valid
/// AES-GCM ciphertext and we leave it alone; a failure means it is plaintext and
/// gets passed through <see cref="ICredentialEncryptor.EnsureEncrypted"/>.
///
/// Runs once at host startup, before background services begin polling.
/// Idempotent — safe to run repeatedly; on a clean install it touches nothing.
/// </summary>
public class PlaintextCredentialMigrator : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlaintextCredentialMigrator> _logger;

    public PlaintextCredentialMigrator(IServiceScopeFactory scopeFactory, ILogger<PlaintextCredentialMigrator> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Mail2SnmpDbContext>();
            var encryptor = scope.ServiceProvider.GetRequiredService<ICredentialEncryptor>();

            // Skip if the schema has not been created yet (first-run before migrations).
            if (!await db.Database.CanConnectAsync(cancellationToken))
                return;

            int rewritten = 0;

            // Mailboxes
            var mailboxes = await db.Mailboxes.ToListAsync(cancellationToken);
            foreach (var m in mailboxes)
            {
                if (string.IsNullOrEmpty(m.EncryptedPassword)) continue;
                if (encryptor.TryDecrypt(m.EncryptedPassword, out _)) continue;
                m.EncryptedPassword = encryptor.Encrypt(m.EncryptedPassword);
                rewritten++;
            }

            // SnmpTargets — both auth and priv passwords
            var snmpTargets = await db.SnmpTargets.ToListAsync(cancellationToken);
            foreach (var t in snmpTargets)
            {
                if (!string.IsNullOrEmpty(t.EncryptedAuthPassword) && !encryptor.TryDecrypt(t.EncryptedAuthPassword, out _))
                {
                    t.EncryptedAuthPassword = encryptor.Encrypt(t.EncryptedAuthPassword);
                    rewritten++;
                }
                if (!string.IsNullOrEmpty(t.EncryptedPrivPassword) && !encryptor.TryDecrypt(t.EncryptedPrivPassword, out _))
                {
                    t.EncryptedPrivPassword = encryptor.Encrypt(t.EncryptedPrivPassword);
                    rewritten++;
                }
            }

            // WebhookTargets
            var webhookTargets = await db.WebhookTargets.ToListAsync(cancellationToken);
            foreach (var w in webhookTargets)
            {
                if (string.IsNullOrEmpty(w.EncryptedSecret)) continue;
                if (encryptor.TryDecrypt(w.EncryptedSecret, out _)) continue;
                w.EncryptedSecret = encryptor.Encrypt(w.EncryptedSecret);
                rewritten++;
            }

            if (rewritten > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogWarning(
                    "PlaintextCredentialMigrator: re-encrypted {Count} credential field(s) that were previously stored as plaintext. " +
                    "Database is now consistent with the AES-GCM encryption policy.",
                    rewritten);
            }
            else
            {
                _logger.LogInformation("PlaintextCredentialMigrator: no plaintext credentials found. Skipping.");
            }
        }
        catch (Exception ex)
        {
            // Never block startup on this — credentials that fail to migrate now will be
            // re-attempted on the next start. Log loudly so an operator notices.
            _logger.LogError(ex, "PlaintextCredentialMigrator failed; some credentials may still be stored as plaintext.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
