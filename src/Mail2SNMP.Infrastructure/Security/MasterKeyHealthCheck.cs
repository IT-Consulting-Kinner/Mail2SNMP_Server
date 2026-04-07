using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Mail2SNMP.Infrastructure.Security;

/// <summary>
/// Health check that verifies the master encryption key is functional.
/// Reports Unhealthy if probe-decrypt fails, which blocks /health/ready (v5.8).
///
/// N11: Also probes a real encrypted credential from the database (if any exist)
/// to detect cluster master-key drift — i.e. an instance was started with a
/// different master.key than the one used to encrypt the existing rows. The
/// pure self-encrypt-self-decrypt probe always succeeds in that case (the key
/// can decrypt what it just encrypted) and would not surface the mistake.
/// </summary>
public class MasterKeyHealthCheck : IHealthCheck
{
    private readonly ICredentialEncryptor _encryptor;
    private readonly Mail2SnmpDbContext _db;

    public MasterKeyHealthCheck(ICredentialEncryptor encryptor, Mail2SnmpDbContext db)
    {
        _encryptor = encryptor;
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // Step 1: self probe — does the encryptor round-trip its own output?
        try
        {
            var probe = _encryptor.Encrypt("health-check-probe");
            if (!_encryptor.TryDecrypt(probe, out var result) || result != "health-check-probe")
            {
                return HealthCheckResult.Unhealthy(
                    "Master key probe-decrypt returned incorrect result. The encryption subsystem is broken.");
            }
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Master key probe-decrypt failed. Check the master key configuration.", ex);
        }

        // Step 2 (N11): cluster drift probe. Pick the first non-empty encrypted
        // credential we can find in the database and try to decrypt it. A failure
        // here means our local key is different from the key the cluster used to
        // store credentials — typically a container with the wrong master.key
        // mounted. We deliberately do not throw on a clean DB.
        try
        {
            var sampleMailbox = await _db.Mailboxes
                .AsNoTracking()
                .Where(m => m.EncryptedPassword != null && m.EncryptedPassword != "")
                .Select(m => new { m.Name, m.EncryptedPassword })
                .FirstOrDefaultAsync(cancellationToken);
            if (sampleMailbox != null && !_encryptor.TryDecrypt(sampleMailbox.EncryptedPassword!, out _))
            {
                return HealthCheckResult.Unhealthy(
                    $"Master key drift detected: cannot decrypt the existing credential for mailbox '{sampleMailbox.Name}'. " +
                    "This instance is configured with a different master.key than the one used to encrypt the database. " +
                    "Mount the correct key file or run 'mail2snmp credentials rotate-key' from a node that owns the current key.");
            }

            var sampleSnmp = await _db.SnmpTargets
                .AsNoTracking()
                .Where(t => t.EncryptedAuthPassword != null && t.EncryptedAuthPassword != "")
                .Select(t => new { t.Name, t.EncryptedAuthPassword })
                .FirstOrDefaultAsync(cancellationToken);
            if (sampleSnmp != null && !_encryptor.TryDecrypt(sampleSnmp.EncryptedAuthPassword!, out _))
            {
                return HealthCheckResult.Unhealthy(
                    $"Master key drift detected: cannot decrypt the existing SNMPv3 auth password for target '{sampleSnmp.Name}'. " +
                    "Mount the correct master.key on this instance.");
            }
        }
        catch (Exception ex)
        {
            // Never fail the health check on transient DB errors — only the
            // explicit decrypt-failure path above is treated as unhealthy.
            return HealthCheckResult.Degraded(
                "Master key self-probe succeeded but the cluster drift probe could not query the database.", ex);
        }

        return HealthCheckResult.Healthy("Master key encryption is operational and consistent with stored credentials.");
    }
}
