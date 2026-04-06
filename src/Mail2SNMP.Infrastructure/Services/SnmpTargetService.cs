using System.Net;
using System.Net.Sockets;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;
using Mail2SNMP.Core.Exceptions;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Infrastructure.Services;

/// <summary>
/// CRUD implementation for SNMP trap targets with credential encryption for v3 passwords.
/// </summary>
public class SnmpTargetService : ISnmpTargetService
{
    private readonly Mail2SnmpDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICredentialEncryptor _encryptor;
    private readonly ILogger<SnmpTargetService> _logger;

    public SnmpTargetService(Mail2SnmpDbContext db, IAuditService audit, ICredentialEncryptor encryptor, ILogger<SnmpTargetService> logger)
    {
        _db = db;
        _audit = audit;
        _encryptor = encryptor;
        _logger = logger;
    }

    /// <summary>
    /// Returns all SNMP targets ordered by name.
    /// </summary>
    public async Task<IReadOnlyList<SnmpTarget>> GetAllAsync(CancellationToken ct = default)
        => await _db.SnmpTargets.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);

    /// <summary>
    /// Returns a single SNMP target by its identifier, or <c>null</c> if not found.
    /// </summary>
    public async Task<SnmpTarget?> GetByIdAsync(int id, CancellationToken ct = default)
        => await _db.SnmpTargets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);

    /// <summary>
    /// Creates a new SNMP trap target.
    /// </summary>
    public async Task<SnmpTarget> CreateAsync(SnmpTarget target, CancellationToken ct = default)
    {
        _db.SnmpTargets.Add(target);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(Models.Enums.ActorType.System, "system", "SnmpTarget.Created", "SnmpTarget", target.Id.ToString(), ct: ct);
        return target;
    }

    /// <summary>
    /// Updates an existing SNMP target configuration.
    /// </summary>
    public async Task<SnmpTarget> UpdateAsync(SnmpTarget target, CancellationToken ct = default)
    {
        var existing = _db.ChangeTracker.Entries<SnmpTarget>()
            .FirstOrDefault(e => e.Entity.Id == target.Id);
        if (existing != null)
            existing.CurrentValues.SetValues(target);
        else
            _db.SnmpTargets.Update(target);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(Models.Enums.ActorType.System, "system", "SnmpTarget.Updated", "SnmpTarget", target.Id.ToString(), ct: ct);
        return target;
    }

    /// <summary>
    /// Deletes an SNMP target by its identifier. Throws <see cref="DependencyException"/>
    /// if any job still references this target.
    /// </summary>
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var target = await _db.SnmpTargets.FindAsync(new object[] { id }, ct)
            ?? throw new KeyNotFoundException($"SNMP target {id} not found.");

        var referencingJoin = await _db.JobSnmpTargets
            .Include(jst => jst.Job)
            .FirstOrDefaultAsync(jst => jst.SnmpTargetId == id, ct);
        if (referencingJoin != null)
            throw new DependencyException($"SNMP Target '{target.Name}' cannot be deleted — it is used by Job '{referencingJoin.Job.Name}'.");

        _db.SnmpTargets.Remove(target);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(Models.Enums.ActorType.System, "system", "SnmpTarget.Deleted", "SnmpTarget", id.ToString(), ct: ct);
    }

    /// <summary>
    /// Sends a test SNMP trap to the specified target using v1, v2c, or v3 depending on configuration.
    /// </summary>
    public async Task<bool> TestAsync(int id, CancellationToken ct = default)
    {
        var target = await GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"SNMP target {id} not found.");

        _logger.LogInformation("Sending test trap to SNMP target {Name} ({Host}:{Port} v{Version})",
            target.Name, target.Host, target.Port, target.Version);

        var testOid = new ObjectIdentifier(target.EnterpriseTrapOid ?? "1.3.6.1.4.1.99999.1.1");
        var varbinds = new List<Variable>
        {
            new(new ObjectIdentifier(testOid + ".1"), new OctetString("Mail2SNMP Test Trap")),
            new(new ObjectIdentifier(testOid + ".2"), new OctetString($"Test from target '{target.Name}'")),
            new(new ObjectIdentifier(testOid + ".3"), new OctetString(DateTime.UtcNow.ToString("O")))
        };

        try
        {
            var endpoint = new IPEndPoint(IPAddress.Parse(target.Host), target.Port);

            if (target.Version == SnmpVersion.V1)
            {
                Messenger.SendTrapV1(endpoint, IPAddress.Loopback,
                    new OctetString(target.CommunityString ?? "public"),
                    testOid, GenericCode.EnterpriseSpecific, 0, 0, varbinds);
            }
            else if (target.Version == SnmpVersion.V2c)
            {
                Messenger.SendTrapV2(0, VersionCode.V2, endpoint,
                    new OctetString(target.CommunityString ?? "public"),
                    testOid, 0, varbinds);
            }
            else
            {
                // SNMP v3 with USM
                SendV3TestTrap(target, endpoint, testOid, varbinds);
            }

            _logger.LogInformation("Test trap sent successfully to {Name} ({Host}:{Port})", target.Name, target.Host, target.Port);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Test trap failed for SNMP target {Name}: {Error}", target.Name, ex.Message);
            throw new InvalidOperationException($"SNMP test trap failed: {ex.Message}", ex);
        }
    }

    private void SendV3TestTrap(SnmpTarget target, IPEndPoint endpoint, ObjectIdentifier oid, List<Variable> varbinds)
    {
        // Resolve auth provider
        IAuthenticationProvider authProvider;
        if (target.AuthProtocol != AuthProtocol.None && !string.IsNullOrEmpty(target.EncryptedAuthPassword))
        {
            var authPassword = DecryptCredential(target.EncryptedAuthPassword);
            var authPassPhrase = new OctetString(authPassword);
            authProvider = target.AuthProtocol switch
            {
                AuthProtocol.MD5 => new MD5AuthenticationProvider(authPassPhrase),
                AuthProtocol.SHA => new SHA1AuthenticationProvider(authPassPhrase),
                AuthProtocol.SHA256 => new SHA256AuthenticationProvider(authPassPhrase),
                AuthProtocol.SHA512 => new SHA512AuthenticationProvider(authPassPhrase),
                _ => DefaultAuthenticationProvider.Instance
            };
        }
        else
        {
            authProvider = DefaultAuthenticationProvider.Instance;
        }

        // Resolve privacy provider
        IPrivacyProvider privProvider;
        if (target.PrivProtocol != PrivProtocol.None && !string.IsNullOrEmpty(target.EncryptedPrivPassword))
        {
            var privPassword = DecryptCredential(target.EncryptedPrivPassword);
            var privPassPhrase = new OctetString(privPassword);
            privProvider = target.PrivProtocol switch
            {
                PrivProtocol.DES => new DESPrivacyProvider(privPassPhrase, authProvider),
                PrivProtocol.AES128 => new AESPrivacyProvider(privPassPhrase, authProvider),
                PrivProtocol.AES256 => new AES256PrivacyProvider(privPassPhrase, authProvider),
                _ => new DefaultPrivacyProvider(authProvider)
            };
        }
        else
        {
            privProvider = new DefaultPrivacyProvider(authProvider);
        }

        // Resolve engine ID
        OctetString engineId;
        if (!string.IsNullOrEmpty(target.EngineId))
        {
            try
            {
                engineId = new OctetString(ByteTool.Convert(target.EngineId));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                // T2: catch only the expected parse errors so unrelated exceptions
                // (OOM, ThreadAbort, etc.) still propagate as they should.
                _logger.LogWarning(ex, "Invalid EngineId format for target {Name}. Using default.", target.Name);
                engineId = new OctetString(new byte[] { 0x80, 0x00, 0x1F, 0x88, 0x80, 0xE9, 0x63, 0x00, 0x00, 0xD6, 0x1F, 0xF4, 0x49 });
            }
        }
        else
        {
            engineId = new OctetString(new byte[] { 0x80, 0x00, 0x1F, 0x88, 0x80, 0xE9, 0x63, 0x00, 0x00, 0xD6, 0x1F, 0xF4, 0x49 });
        }

        // Construct v3 trap message directly (Messenger.SendTrapV2 does not support v3)
        var securityName = new OctetString(target.SecurityName ?? string.Empty);
        var requestId = Environment.TickCount;
        var messageId = requestId + 1;
        var trap = new TrapV2Message(
            VersionCode.V3,
            messageId,
            requestId,
            securityName,
            oid,
            0,
            varbinds,
            privProvider,
            0x10000,
            engineId,
            0, 0);

        // Send via UDP
        using var udpClient = new UdpClient();
        var bytes = trap.ToBytes();
        udpClient.Send(bytes, bytes.Length, endpoint);
    }

    /// <summary>
    /// Decrypts a stored SNMP credential. Fails fast on master key mismatch instead of
    /// silently falling back to the encrypted ciphertext as a "password" — that
    /// fallback would mask a real configuration problem (wrong master key) as an
    /// "auth failed" error and is a security/diagnostics anti-pattern. The behaviour
    /// is now consistent with <c>MailboxService.TestConnectionAsync</c>.
    /// </summary>
    private string DecryptCredential(string encrypted)
    {
        try
        {
            return _encryptor.Decrypt(encrypted);
        }
        catch (Exception decryptEx)
        {
            _logger.LogError(decryptEx,
                "Failed to decrypt SNMP credential. " +
                "This indicates a master key mismatch. Re-enter the password via the Web UI " +
                "or restore the correct master.key file.");
            throw new InvalidOperationException(
                "Cannot decrypt SNMP credential. The master key may have changed. " +
                "Re-enter the password and try again.",
                decryptEx);
        }
    }
}
