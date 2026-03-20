using Mail2SNMP.Core.Exceptions;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Entities;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Infrastructure.Services;

/// <summary>
/// CRUD implementation for IMAP mailbox configurations with credential encryption.
/// </summary>
public class MailboxService : IMailboxService
{
    private readonly Mail2SnmpDbContext _db;
    private readonly ILicenseProvider _license;
    private readonly IAuditService _audit;
    private readonly ICredentialEncryptor _credentialEncryptor;
    private readonly ILogger<MailboxService> _logger;

    public MailboxService(Mail2SnmpDbContext db, ILicenseProvider license, IAuditService audit, ICredentialEncryptor credentialEncryptor, ILogger<MailboxService> logger)
    {
        _db = db;
        _license = license;
        _audit = audit;
        _credentialEncryptor = credentialEncryptor;
        _logger = logger;
    }

    /// <summary>
    /// Returns all mailbox configurations ordered by name.
    /// </summary>
    public async Task<IReadOnlyList<Mailbox>> GetAllAsync(CancellationToken ct = default)
        => await _db.Mailboxes.AsNoTracking().OrderBy(m => m.Name).ToListAsync(ct);

    /// <summary>
    /// Returns a single mailbox by its identifier, or <c>null</c> if not found.
    /// </summary>
    public async Task<Mailbox?> GetByIdAsync(int id, CancellationToken ct = default)
        => await _db.Mailboxes.FindAsync(new object[] { id }, ct);

    /// <summary>
    /// Creates a new mailbox after verifying the license-enforced mailbox limit has not been reached.
    /// </summary>
    public async Task<Mailbox> CreateAsync(Mailbox mailbox, CancellationToken ct = default)
    {
        var count = await _db.Mailboxes.CountAsync(ct);
        var max = _license.GetLimit("maxmailboxes");
        if (count >= max)
            throw new InvalidOperationException($"Community Edition limit: max {max} mailboxes. Upgrade to Enterprise for unlimited.");

        _db.Mailboxes.Add(mailbox);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(Models.Enums.ActorType.User, "system", "Mailbox.Created", "Mailbox", mailbox.Id.ToString(), ct: ct);
        return mailbox;
    }

    /// <summary>
    /// Updates an existing mailbox configuration.
    /// </summary>
    public async Task<Mailbox> UpdateAsync(Mailbox mailbox, CancellationToken ct = default)
    {
        var existing = _db.ChangeTracker.Entries<Mailbox>()
            .FirstOrDefault(e => e.Entity.Id == mailbox.Id);
        if (existing != null)
            existing.CurrentValues.SetValues(mailbox);
        else
            _db.Mailboxes.Update(mailbox);

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(Models.Enums.ActorType.User, "system", "Mailbox.Updated", "Mailbox", mailbox.Id.ToString(), ct: ct);
        return mailbox;
    }

    /// <summary>
    /// Deletes a mailbox by its identifier. Throws <see cref="DependencyException"/>
    /// if any job still references this mailbox.
    /// </summary>
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var mailbox = await _db.Mailboxes.FindAsync(new object[] { id }, ct)
            ?? throw new KeyNotFoundException($"Mailbox {id} not found.");

        var referencingJob = await _db.Jobs.FirstOrDefaultAsync(j => j.MailboxId == id, ct);
        if (referencingJob != null)
            throw new DependencyException($"Mailbox '{mailbox.Name}' cannot be deleted — it is used by Job '{referencingJob.Name}'.");

        _db.Mailboxes.Remove(mailbox);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(Models.Enums.ActorType.User, "system", "Mailbox.Deleted", "Mailbox", id.ToString(), ct: ct);
    }

    /// <summary>
    /// Attempts an IMAP connect-and-authenticate against the mailbox with a 10-second timeout.
    /// </summary>
    public async Task<bool> TestConnectionAsync(int id, CancellationToken ct = default)
    {
        var mailbox = await GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Mailbox {id} not found.");

        _logger.LogInformation("Testing IMAP connection for mailbox {Name} ({Host}:{Port})",
            mailbox.Name, mailbox.Host, mailbox.Port);

        using var client = new ImapClient();
        try
        {
            var sslOptions = mailbox.UseSsl
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTlsWhenAvailable;

            // 10s connect timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            await client.ConnectAsync(mailbox.Host, mailbox.Port, sslOptions, timeoutCts.Token);

            // Decrypt password
            string password;
            try
            {
                password = _credentialEncryptor.Decrypt(mailbox.EncryptedPassword);
            }
            catch
            {
                _logger.LogWarning("Failed to decrypt password for mailbox {Name}. Testing with raw value.", mailbox.Name);
                password = mailbox.EncryptedPassword;
            }

            await client.AuthenticateAsync(mailbox.Username, password, timeoutCts.Token);

            _logger.LogInformation("IMAP connection test successful for mailbox {Name}", mailbox.Name);
            await client.DisconnectAsync(true, CancellationToken.None);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IMAP connection test failed for mailbox {Name}: {Error}", mailbox.Name, ex.Message);
            throw new InvalidOperationException($"Connection failed: {ex.Message}", ex);
        }
    }
}
