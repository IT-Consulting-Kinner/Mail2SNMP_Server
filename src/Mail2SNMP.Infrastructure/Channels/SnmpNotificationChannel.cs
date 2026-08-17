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
    private readonly IDeadLetterService _deadLetterService;
    private readonly ILogger<SnmpNotificationChannel> _logger;

    /// <summary>
    /// Stable channel discriminator ('snmp') used to select this channel from the registered
    /// <see cref="INotificationChannel"/> set. Must remain constant: persisted job/channel
    /// assignments reference channels by this value.
    /// </summary>
    public string ChannelName => "snmp";

    // Mail2SNMP MIB OIDs (Enterprise Number 61376, registered to IT-Consulting Kinner)

    /// <summary>
    /// Trap OID for the <c>mail2SNMPEventCreatedNotification</c> notification, sent when a new
    /// event is raised. Carries the eventID, eventName, eventSeverity, and eventMessage varbinds.
    /// </summary>
    public const string EventCreatedOid   = "1.3.6.1.4.1.61376.1.2.0.1";

    /// <summary>
    /// Trap OID for the <c>mail2SNMPEventConfirmedNotification</c> notification, sent when an
    /// event is acknowledged. Carries only the eventID varbind.
    /// </summary>
    public const string EventConfirmedOid = "1.3.6.1.4.1.61376.1.2.0.2";

    /// <summary>
    /// Trap OID for the <c>mail2SNMPKeepAliveNotification</c> heartbeat trap (no varbinds),
    /// sent to targets with keep-alive enabled so monitoring systems can detect a dead server.
    /// </summary>
    public const string KeepAliveOid      = "1.3.6.1.4.1.61376.1.2.0.3";

    /// <summary>
    /// Trap OID for the <c>mail2SNMPUpdateNotification</c> notification, sent to advertise that a
    /// newer Mail2SNMP release is available; version details are carried in the eventMessage varbind.
    /// </summary>
    public const string UpdateOid         = "1.3.6.1.4.1.61376.1.2.0.4";

    // MIB object identifiers (varbinds)
    private const string EventIDOid       = "1.3.6.1.4.1.61376.1.1.1.1.1";
    private const string EventNameOid     = "1.3.6.1.4.1.61376.1.1.1.1.2";
    private const string EventSeverityOid = "1.3.6.1.4.1.61376.1.1.1.1.3";
    private const string EventMessageOid  = "1.3.6.1.4.1.61376.1.1.1.1.4";

    /// <summary>
    /// Initializes the channel with its collaborators.
    /// </summary>
    /// <param name="db">Database context used to load active SNMP targets and resolve job-assigned targets.</param>
    /// <param name="encryptor">Decryptor for the AES-GCM-encrypted community strings and SNMPv3 auth/priv passwords at send time.</param>
    /// <param name="license">License gate; SNMPv3 (AuthPriv) targets are skipped unless an Enterprise license is active.</param>
    /// <param name="templateEngine">Used to render and length-truncate strings so they fit SNMP <c>OctetString</c> limits.</param>
    /// <param name="floodProtection">Per-target rate limiter enforcing each target's maximum traps-per-minute.</param>
    /// <param name="dedupCache">Suppresses duplicate traps for the same target/event pair.</param>
    /// <param name="deadLetterService">UC-3: queues failed trap sends for automatic retry, mirroring the webhook channel.</param>
    /// <param name="logger">Diagnostic logger; trap delivery failures are logged rather than thrown (traps are fire-and-forget UDP).</param>
    public SnmpNotificationChannel(
        Mail2SnmpDbContext db,
        ICredentialEncryptor encryptor,
        ILicenseProvider license,
        TemplateEngine templateEngine,
        FloodProtectionService floodProtection,
        NotificationDedupCache dedupCache,
        IDeadLetterService deadLetterService,
        ILogger<SnmpNotificationChannel> logger)
    {
        _db = db;
        _encryptor = encryptor;
        _license = license;
        _templateEngine = templateEngine;
        _floodProtection = floodProtection;
        _dedupCache = dedupCache;
        _deadLetterService = deadLetterService;
        _logger = logger;
    }

    // AR-2: the legacy broadcast SendAsync(NotificationContext) was removed — it had
    // zero call sites (all real dispatch is per-target via the job's assignments)
    // and its presence suggested a working generic path that did not exist.

    /// <summary>
    /// Sends an SNMP trap to a specific target (per-job assignment). Applies dedup and rate-limiting.
    /// </summary>
    /// <returns>
    /// FN-3: <c>true</c> when the trap left the process (or an earlier send already
    /// covered this event/target via notification-dedup); <c>false</c> on rate-limit
    /// drop or any send failure — so callers never mark an event Notified when
    /// nothing was actually dispatched.
    /// </returns>
    public async Task<bool> SendToSnmpTargetAsync(NotificationContext context, SnmpTarget target, CancellationToken ct = default)
    {
        var targetKey = $"snmp:{target.Id}";

        // UC-3: a dead-letter redelivery must bypass the notification-dedup check —
        // the ORIGINAL (failed) attempt already registered the event/target pair,
        // so the cache would otherwise report the retry as "already covered".
        if (!context.IsRedelivery && _dedupCache.IsDuplicate(targetKey, context.EventId))
        {
            _logger.LogDebug("Notification dedup: skipping duplicate SNMP trap to {Target} for event {EventId}", target.Name, context.EventId);
            // An earlier send already covered this event/target pair — delivered.
            return true;
        }

        if (_floodProtection.IsRateLimited(targetKey, target.MaxTrapsPerMinute))
        {
            Services.Mail2SnmpMetrics.TrapsRateLimited.Inc();
            Services.Mail2SnmpMetrics.RateLimitHits.WithLabels("snmp-target").Inc();
            _logger.LogWarning("SNMP trap to {Target} rate-limited ({Max}/min)", target.Name, target.MaxTrapsPerMinute);
            // Deliberate policy drop — NOT dead-lettered (a delayed re-send would
            // defeat the flood protection's purpose).
            return false;
        }

        try
        {
            var sent = await SendTrapAsync(target, context);
            if (sent)
            {
                Services.Mail2SnmpMetrics.NotificationsSent.WithLabels("snmp").Inc();
                Services.Mail2SnmpMetrics.SnmpTrapsSent.WithLabels(target.Name, target.Version.ToString()).Inc();
                _logger.LogInformation("SNMP trap sent to {Host}:{Port} (v{Version}) for event {EventId}",
                    target.Host, target.Port, target.Version, context.EventId);
            }
            else
            {
                Services.Mail2SnmpMetrics.NotificationsFailed.WithLabels("snmp").Inc();
                await CreateDeadLetterAsync(target, context, "Trap dropped (DNS/credentials/licence — see log)", ct);
            }
            return sent;
        }
        catch (Exception ex)
        {
            Services.Mail2SnmpMetrics.NotificationsFailed.WithLabels("snmp").Inc();
            _logger.LogError(ex, "Failed to send SNMP trap to {Host}:{Port}: {Message}", target.Host, target.Port, ex.Message);
            await CreateDeadLetterAsync(target, context, ex.Message, ct);
            return false;
        }
    }

    /// <summary>
    /// UC-3: queues a failed trap send for automatic retry, mirroring the webhook
    /// channel's dead-lettering. The serialized <see cref="NotificationContext"/>
    /// is stored so the trap can be rebuilt verbatim on retry. Never called for a
    /// redelivery (the retry service owns the existing entry's lifecycle) and
    /// never for synthetic test events (negative EventId — no Event row exists
    /// for the FK).
    /// </summary>
    private async Task CreateDeadLetterAsync(Models.Entities.SnmpTarget target, NotificationContext context, string error, CancellationToken ct)
    {
        if (context.IsRedelivery || context.EventId <= 0) return;
        try
        {
            await _deadLetterService.CreateAsync(new Models.Entities.DeadLetterEntry
            {
                SnmpTargetId = target.Id,
                EventId = context.EventId,
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(context),
                LastError = error,
                AttemptCount = 1,
                Status = Models.Enums.DeadLetterStatus.Pending
            }, ct);
        }
        catch (Exception dlEx)
        {
            // Dead-lettering is best-effort: a failure here must not mask the
            // original send failure or crash the notification loop.
            _logger.LogError(dlEx, "Failed to dead-letter SNMP trap for target {Name}, event {EventId}", target.Name, context.EventId);
        }
    }

    /// <summary>
    /// Constructs and sends an EventCreated SNMP trap (v1, v2c, or v3) to a single target.
    /// Uses the official Mail2SNMP MIB OIDs (eventID, eventName, eventSeverity, eventMessage).
    /// </summary>
    private Task<bool> SendTrapAsync(Models.Entities.SnmpTarget target, NotificationContext context)
    {
        var trapOid = new ObjectIdentifier(target.EnterpriseTrapOid ?? EventCreatedOid);

        // Use the email subject as the eventMessage (fallback to "From: subject" combo)
        var message = !string.IsNullOrWhiteSpace(context.Subject)
            ? context.Subject
            : $"{context.From}: {context.JobName}";

        // MIB-conformant varbinds for mail2SNMPEventCreatedNotification:
        //   eventID, eventName, eventSeverity, eventMessage
        var varbinds = new List<Variable>
        {
            // UC-7 (verified fix): clamp BOTH bounds. Synthetic test events use a large
            // negative EventId; the old upper-bound-only Min let the unchecked cast
            // wrap it into an arbitrary int. Test traps now report eventID 0.
            new(new ObjectIdentifier(EventIDOid),       new Integer32((int)Math.Clamp(context.EventId, 0, int.MaxValue))),
            new(new ObjectIdentifier(EventNameOid),     new OctetString(_templateEngine.TruncateForSnmp(context.JobName))),
            new(new ObjectIdentifier(EventSeverityOid), new OctetString(context.Severity.ToString())),
            new(new ObjectIdentifier(EventMessageOid),  new OctetString(_templateEngine.TruncateForSnmp(message)))
        };

        return SendVarbindsAsync(target, trapOid, varbinds);
    }

    /// <summary>
    /// Sends an mail2SNMPEventConfirmedNotification trap (eventID only) to the SNMP targets
    /// that are assigned to the originating job. Falls back to all active targets only when
    /// the event cannot be resolved (defensive). Triggered when an event is acknowledged.
    /// </summary>
    public async Task SendEventConfirmedAsync(long eventId, CancellationToken ct = default)
    {
        // Resolve the assigned SNMP targets via the event's job. We deliberately do NOT
        // broadcast to every active target — only the targets the job was configured for
        // should be informed about an acknowledge for that event.
        // UC-4 (verified fix): apply the same severity routing as the original event
        // dispatch — a target that never received the EventCreated trap (severity below
        // its threshold) must not receive a confirm trap referencing an eventID the
        // receiver has never seen.
        var assignedTargets = await _db.Events
            .Where(e => e.Id == eventId)
            .SelectMany(e => e.Job.JobSnmpTargets
                .Where(jst => jst.SnmpTarget.IsActive && e.Severity >= jst.SnmpTarget.MinSeverity))
            .Select(jst => jst.SnmpTarget)
            .ToListAsync(ct);

        if (assignedTargets.Count == 0)
        {
            _logger.LogWarning(
                "EventConfirmed trap for event {EventId}: no assigned SNMP targets found. " +
                "The acknowledge is recorded in the database but no monitoring system was notified.",
                eventId);
            return;
        }

        var trapOid = new ObjectIdentifier(EventConfirmedOid);
        var failed = 0;

        foreach (var target in assignedTargets)
        {
            try
            {
                var varbinds = new List<Variable>
                {
                    new(new ObjectIdentifier(EventIDOid), new Integer32((int)Math.Min(eventId, int.MaxValue)))
                };
                await SendVarbindsAsync(target, trapOid, varbinds);
                _logger.LogInformation("EventConfirmed trap sent to {Host}:{Port} for event {EventId}", target.Host, target.Port, eventId);
            }
            catch (Exception ex)
            {
                failed++;
                // H14: SNMP traps have no retry/dead-letter mechanism — log loudly so the
                // operator can manually re-acknowledge or investigate.
                _logger.LogError(ex,
                    "EventConfirmed trap delivery FAILED to {Host}:{Port} for event {EventId}. " +
                    "This trap will NOT be retried automatically. Verify connectivity to the monitoring system.",
                    target.Host, target.Port, eventId);
            }
        }

        if (failed > 0)
        {
            _logger.LogWarning(
                "EventConfirmed for event {EventId}: {Failed} of {Total} target(s) failed.",
                eventId, failed, assignedTargets.Count);
        }
    }

    /// <summary>
    /// Sends an mail2SNMPKeepAliveNotification trap (no varbinds) to all SNMP targets that have SendKeepAlive enabled.
    /// </summary>
    public async Task SendKeepAliveAsync(CancellationToken ct = default)
    {
        var targets = await _db.SnmpTargets.Where(t => t.IsActive && t.SendKeepAlive).ToListAsync(ct);
        var trapOid = new ObjectIdentifier(KeepAliveOid);

        foreach (var target in targets)
        {
            try
            {
                await SendVarbindsAsync(target, trapOid, new List<Variable>());
                _logger.LogDebug("KeepAlive trap sent to {Host}:{Port}", target.Host, target.Port);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send KeepAlive trap to {Host}:{Port}", target.Host, target.Port);
            }
        }
    }

    /// <summary>
    /// Sends an mail2SNMPUpdateNotification trap to all active SNMP targets, carrying current/available
    /// version information in the eventMessage varbind for compatibility with the existing MIB.
    /// </summary>
    public async Task SendUpdateAvailableAsync(UpdateInfo info, CancellationToken ct = default)
    {
        var targets = await _db.SnmpTargets.Where(t => t.IsActive).ToListAsync(ct);
        var trapOid = new ObjectIdentifier(UpdateOid);

        var message = $"Mail2SNMP update available: {info.CurrentVersion} → {info.AvailableVersion} ({info.PublishDate}) {info.DownloadUrl}";

        foreach (var target in targets)
        {
            try
            {
                var varbinds = new List<Variable>
                {
                    new(new ObjectIdentifier(EventNameOid),     new OctetString("Mail2SNMP Update Available")),
                    new(new ObjectIdentifier(EventSeverityOid), new OctetString("Information")),
                    new(new ObjectIdentifier(EventMessageOid),  new OctetString(_templateEngine.TruncateForSnmp(message)))
                };
                await SendVarbindsAsync(target, trapOid, varbinds);
                _logger.LogInformation("Update trap sent to {Host}:{Port} ({Available})", target.Host, target.Port, info.AvailableVersion);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send Update trap to {Host}:{Port}", target.Host, target.Port);
            }
        }
    }

    /// <summary>
    /// Internal helper that performs the actual UDP send for v1/v2c/v3.
    /// FN-3: returns <c>false</c> on every silent-drop path (DNS failure,
    /// credential-decrypt failure, v3-under-Community skip) and <c>true</c> only
    /// when the trap was actually handed to the network stack, so callers can
    /// distinguish "attempt completed" from "nothing was sent".
    /// </summary>
    private async Task<bool> SendVarbindsAsync(Models.Entities.SnmpTarget target, ObjectIdentifier trapOid, List<Variable> varbinds)
    {
        IPAddress ipAddress;
        if (!IPAddress.TryParse(target.Host, out ipAddress!))
        {
            // PF-6: resolve hostname asynchronously with a hard 5 s timeout. The
            // previous synchronous Dns.GetHostAddresses pinned the calling consumer
            // thread for the OS resolver timeout (seconds) whenever the DNS server
            // was slow or unreachable, serially stalling ingestion for every
            // hostname-configured target. Failures are logged and the trap dropped.
            try
            {
                using var dnsCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var addresses = await Dns.GetHostAddressesAsync(target.Host, dnsCts.Token);
                if (addresses.Length == 0)
                {
                    _logger.LogWarning(
                        "DNS lookup for SNMP target {Name} ({Host}) returned no addresses. Trap dropped.",
                        target.Name, target.Host);
                    return false;
                }
                ipAddress = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            ?? addresses[0];
            }
            catch (Exception dnsEx)
            {
                _logger.LogError(dnsEx,
                    "DNS lookup failed for SNMP target {Name} ({Host}): {Message}. Trap dropped.",
                    target.Name, target.Host, dnsEx.Message);
                return false;
            }
        }
        var endpoint = new IPEndPoint(ipAddress, target.Port);

        // R2: decrypt the community string at the moment of use. The funnel
        // in SnmpTargetService stored AES-GCM ciphertext; passing the literal
        // ciphertext as a community string would silently fail every trap on
        // the receiving side, so a master-key mismatch must surface loudly.
        string community = "public";
        if (!string.IsNullOrEmpty(target.EncryptedCommunityString))
        {
            try { community = _encryptor.Decrypt(target.EncryptedCommunityString); }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to decrypt community string for SNMP target {Name}. Master key mismatch — trap dropped.",
                    target.Name);
                return false;
            }
        }

        if (target.Version == SnmpVersion.V1)
        {
            Messenger.SendTrapV1(endpoint, IPAddress.Loopback,
                new OctetString(community),
                trapOid, GenericCode.EnterpriseSpecific, 0, 0, varbinds);
        }
        else if (target.Version == SnmpVersion.V2c)
        {
            Messenger.SendTrapV2(0, VersionCode.V2, endpoint,
                new OctetString(community),
                trapOid, 0, varbinds);
        }
        else
        {
            if (!_license.IsEnterprise())
            {
                _logger.LogWarning(
                    "SNMP v3 target {Name} skipped — requires an Enterprise license.",
                    target.Name);
                return false;
            }

            SendV3Trap(target, endpoint, trapOid, varbinds);
        }

        return true;
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
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                // N19: catch only the expected parse errors, not arbitrary exceptions
                // (e.g. OutOfMemoryException would still propagate as it should).
                _logger.LogWarning(ex, "Invalid EngineId format for target {Name}. Using default.", target.Name);
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
