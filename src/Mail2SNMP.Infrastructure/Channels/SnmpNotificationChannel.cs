using System.Net;
using System.Net.Sockets;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Core.Services;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Infrastructure.Channels;

/// <summary>
/// Notification channel that delivers events as SNMP traps (v1, v2c, or v3).
/// SNMP v3 (AuthPriv) requires an Enterprise license; v3 targets are skipped
/// when running under a Community license.
/// </summary>
public class SnmpNotificationChannel : INotificationChannel
{
    private readonly Mail2SnmpDbContext _db;
    private readonly ICredentialEncryptor _encryptor;
    private readonly ILicenseProvider _license;
    private readonly TemplateEngine _templateEngine;
    private readonly FloodProtectionService _floodProtection;
    private readonly NotificationDedupCache _dedupCache;
    private readonly ILogger<SnmpNotificationChannel> _logger;

    public string ChannelName => "snmp";

    public SnmpNotificationChannel(
        Mail2SnmpDbContext db,
        ICredentialEncryptor encryptor,
        ILicenseProvider license,
        TemplateEngine templateEngine,
        FloodProtectionService floodProtection,
        NotificationDedupCache dedupCache,
        ILogger<SnmpNotificationChannel> logger)
    {
        _db = db;
        _encryptor = encryptor;
        _license = license;
        _templateEngine = templateEngine;
        _floodProtection = floodProtection;
        _dedupCache = dedupCache;
        _logger = logger;
    }

    /// <summary>
    /// Sends SNMP traps to all active SNMP targets for the given event context.
    /// Applies deduplication and rate-limiting per target before sending.
    /// </summary>
    public async Task SendAsync(NotificationContext context, CancellationToken cancellationToken = default)
    {
        var targets = await _db.SnmpTargets.Where(t => t.IsActive).ToListAsync(cancellationToken);

        foreach (var target in targets)
        {
            var targetKey = $"snmp:{target.Id}";

            if (_dedupCache.IsDuplicate(targetKey, context.EventId))
            {
                _logger.LogDebug("Notification dedup: skipping duplicate SNMP trap to {Target} for event {EventId}", target.Name, context.EventId);
                continue;
            }

            if (_floodProtection.IsRateLimited(targetKey, target.MaxTrapsPerMinute))
            {
                _logger.LogWarning("SNMP trap to {Target} rate-limited ({Max}/min)", target.Name, target.MaxTrapsPerMinute);
                continue;
            }

            try
            {
                await SendTrapAsync(target, context);
                _logger.LogInformation("SNMP trap sent to {Host}:{Port} (v{Version}) for event {EventId}",
                    target.Host, target.Port, target.Version, context.EventId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SNMP trap to {Host}:{Port}: {Message}", target.Host, target.Port, ex.Message);
            }
        }
    }

    /// <summary>
    /// Sends an SNMP trap to a specific target (per-job assignment). Applies dedup and rate-limiting.
    /// </summary>
    public async Task SendToSnmpTargetAsync(NotificationContext context, SnmpTarget target, CancellationToken ct = default)
    {
        var targetKey = $"snmp:{target.Id}";

        if (_dedupCache.IsDuplicate(targetKey, context.EventId))
        {
            _logger.LogDebug("Notification dedup: skipping duplicate SNMP trap to {Target} for event {EventId}", target.Name, context.EventId);
            return;
        }

        if (_floodProtection.IsRateLimited(targetKey, target.MaxTrapsPerMinute))
        {
            _logger.LogWarning("SNMP trap to {Target} rate-limited ({Max}/min)", target.Name, target.MaxTrapsPerMinute);
            return;
        }

        try
        {
            await SendTrapAsync(target, context);
            _logger.LogInformation("SNMP trap sent to {Host}:{Port} (v{Version}) for event {EventId}",
                target.Host, target.Port, target.Version, context.EventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SNMP trap to {Host}:{Port}: {Message}", target.Host, target.Port, ex.Message);
        }
    }

    /// <summary>
    /// Constructs and sends an SNMP trap (v1, v2c, or v3) to a single target.
    /// </summary>
    private Task SendTrapAsync(Models.Entities.SnmpTarget target, NotificationContext context)
    {
        var oid = new ObjectIdentifier(target.EnterpriseTrapOid ?? "1.3.6.1.4.1.99999.1.1");
        var payload = _templateEngine.BuildPayload(context, context.TrapTemplate);

        var varbinds = new List<Variable>
        {
            new(new ObjectIdentifier(oid + ".1"), new OctetString(_templateEngine.TruncateForSnmp(context.JobName))),
            new(new ObjectIdentifier(oid + ".2"), new OctetString(_templateEngine.TruncateForSnmp(context.Subject))),
            new(new ObjectIdentifier(oid + ".3"), new OctetString(_templateEngine.TruncateForSnmp(context.From))),
            new(new ObjectIdentifier(oid + ".4"), new OctetString(context.Severity.ToString())),
            new(new ObjectIdentifier(oid + ".5"), new Integer32(context.HitCount))
        };

        var endpoint = new IPEndPoint(IPAddress.Parse(target.Host), target.Port);

        if (target.Version == SnmpVersion.V1)
        {
            Messenger.SendTrapV1(endpoint, IPAddress.Loopback,
                new OctetString(target.CommunityString ?? "public"),
                oid, GenericCode.EnterpriseSpecific, 0, 0, varbinds);
        }
        else if (target.Version == SnmpVersion.V2c)
        {
            Messenger.SendTrapV2(0, VersionCode.V2, endpoint,
                new OctetString(target.CommunityString ?? "public"),
                oid, 0, varbinds);
        }
        else
        {
            // SNMP v3 requires an Enterprise license
            if (!_license.IsEnterprise())
            {
                _logger.LogWarning(
                    "SNMP v3 target {Name} skipped — requires an Enterprise license. " +
                    "Downgrade the target to v1/v2c or activate an Enterprise license.",
                    target.Name);
                return Task.CompletedTask;
            }

            SendV3Trap(target, endpoint, oid, varbinds);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends an SNMP v3 trap using the USM security model with configurable
    /// authentication (MD5/SHA/SHA256/SHA512) and privacy (DES/AES128/AES256) protocols.
    /// </summary>
    private void SendV3Trap(Models.Entities.SnmpTarget target, IPEndPoint endpoint, ObjectIdentifier oid, List<Variable> varbinds)
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

        // Resolve engine ID (use configured or generate default)
        OctetString engineId;
        if (!string.IsNullOrEmpty(target.EngineId))
        {
            try
            {
                engineId = new OctetString(ByteTool.Convert(target.EngineId));
            }
            catch
            {
                _logger.LogWarning("Invalid EngineId format for target {Name}. Using default.", target.Name);
                engineId = new OctetString(new byte[] { 0x80, 0x00, 0x1F, 0x88, 0x80, 0xE9, 0x63, 0x00, 0x00, 0xD6, 0x1F, 0xF4, 0x49 });
            }
        }
        else
        {
            engineId = new OctetString(new byte[] { 0x80, 0x00, 0x1F, 0x88, 0x80, 0xE9, 0x63, 0x00, 0x00, 0xD6, 0x1F, 0xF4, 0x49 });
        }

        var securityName = new OctetString(target.SecurityName ?? string.Empty);

        _logger.LogDebug("Sending SNMP v3 trap to {Host}:{Port} SecurityName={SecurityName} Auth={Auth} Priv={Priv}",
            target.Host, target.Port, target.SecurityName, target.AuthProtocol, target.PrivProtocol);

        // Construct v3 trap message directly (Messenger.SendTrapV2 does not support v3)
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
    /// Decrypts an encrypted credential. Fails fast on master key mismatch (v5.8: no raw fallback).
    /// </summary>
    private string DecryptCredential(string encrypted)
    {
        try
        {
            return _encryptor.Decrypt(encrypted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to decrypt SNMP credential. " +
                "This indicates a master key mismatch. Re-enter the password via the Web UI or restore the correct master.key file.");
            throw new InvalidOperationException("Credential decryption failed for SNMP target. Check the master key configuration.", ex);
        }
    }
}
