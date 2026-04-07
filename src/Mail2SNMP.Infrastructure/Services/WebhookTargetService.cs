using System.Text;
using System.Text.Json;
using Mail2SNMP.Core.Exceptions;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Infrastructure.Services;

/// <summary>
/// CRUD implementation for webhook targets with secret encryption.
/// </summary>
public class WebhookTargetService : IWebhookTargetService
{
    private readonly Mail2SnmpDbContext _db;
    private readonly IAuditService _audit;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICredentialEncryptor _encryptor;
    private readonly bool _allowPrivateTargets;
    private readonly ILogger<WebhookTargetService> _logger;

    public WebhookTargetService(
        Mail2SnmpDbContext db,
        IAuditService audit,
        IHttpClientFactory httpClientFactory,
        ICredentialEncryptor encryptor,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        ILogger<WebhookTargetService> logger)
    {
        _db = db;
        _audit = audit;
        _httpClientFactory = httpClientFactory;
        _encryptor = encryptor;
        _allowPrivateTargets = configuration.GetValue<bool>("Security:AllowPrivateWebhookTargets");
        _logger = logger;
    }

    /// <summary>
    /// Returns all webhook targets ordered by name.
    /// </summary>
    public async Task<IReadOnlyList<WebhookTarget>> GetAllAsync(CancellationToken ct = default)
        => await _db.WebhookTargets.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);

    /// <summary>
    /// Returns a single webhook target by its identifier, or <c>null</c> if not found.
    /// </summary>
    public async Task<WebhookTarget?> GetByIdAsync(int id, CancellationToken ct = default)
        => await _db.WebhookTargets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);

    /// <summary>
    /// Creates a new webhook target.
    /// </summary>
    public async Task<WebhookTarget> CreateAsync(WebhookTarget target, CancellationToken ct = default)
    {
        // J1: Encrypt the secret before persisting. EnsureEncrypted is idempotent.
        target.EncryptedSecret = _encryptor.EnsureEncrypted(target.EncryptedSecret);

        _db.WebhookTargets.Add(target);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(Models.Enums.ActorType.System, "system", "WebhookTarget.Created", "WebhookTarget", target.Id.ToString(), ct: ct);
        return target;
    }

    /// <summary>
    /// Updates an existing webhook target configuration.
    /// </summary>
    public async Task<WebhookTarget> UpdateAsync(WebhookTarget target, CancellationToken ct = default)
    {
        // J1: Same idempotent encryption funnel.
        target.EncryptedSecret = _encryptor.EnsureEncrypted(target.EncryptedSecret);

        var existing = _db.ChangeTracker.Entries<WebhookTarget>()
            .FirstOrDefault(e => e.Entity.Id == target.Id);
        if (existing != null)
            existing.CurrentValues.SetValues(target);
        else
            _db.WebhookTargets.Update(target);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(Models.Enums.ActorType.System, "system", "WebhookTarget.Updated", "WebhookTarget", target.Id.ToString(), ct: ct);
        return target;
    }

    /// <summary>
    /// Deletes a webhook target by its identifier. Throws <see cref="DependencyException"/>
    /// if any job still references this target.
    /// </summary>
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var target = await _db.WebhookTargets.FindAsync(new object[] { id }, ct)
            ?? throw new KeyNotFoundException($"Webhook target {id} not found.");

        var referencingJoin = await _db.JobWebhookTargets
            .Include(jwt => jwt.Job)
            .FirstOrDefaultAsync(jwt => jwt.WebhookTargetId == id, ct);
        if (referencingJoin != null)
            throw new DependencyException($"Webhook Target '{target.Name}' cannot be deleted — it is used by Job '{referencingJoin.Job.Name}'.");

        _db.WebhookTargets.Remove(target);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(Models.Enums.ActorType.System, "system", "WebhookTarget.Deleted", "WebhookTarget", id.ToString(), ct: ct);
    }

    /// <summary>
    /// Sends a test HTTP POST with a sample JSON payload to the webhook target URL.
    /// </summary>
    public async Task<bool> TestAsync(int id, CancellationToken ct = default)
    {
        var target = await GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Webhook target {id} not found.");

        // R1: SSRF guard. The Test button must not be a backdoor for hitting
        // internal services. Same policy as the actual delivery path.
        if (!Mail2SNMP.Infrastructure.Security.SsrfGuard.IsSafeOutboundUrl(target.Url, _allowPrivateTargets, out var reason))
        {
            _logger.LogWarning(
                "Webhook test for target {Name} ({Url}) blocked by SSRF guard: {Reason}",
                target.Name, target.Url, reason);
            throw new InvalidOperationException(
                $"Webhook target '{target.Name}' was blocked by the SSRF guard: {reason}");
        }

        _logger.LogInformation("Testing webhook for target {Name} ({Url})", target.Name, target.Url);

        var testPayload = new
        {
            test = true,
            source = "Mail2SNMP",
            message = $"Test webhook from Mail2SNMP target '{target.Name}'",
            timestamp = DateTime.UtcNow.ToString("O")
        };

        var json = JsonSerializer.Serialize(testPayload);
        // N2: dispose StringContent and HttpResponseMessage explicitly to release
        // unmanaged buffers/sockets immediately instead of relying on the GC.
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Add custom headers if configured
        if (!string.IsNullOrEmpty(target.Headers))
        {
            try
            {
                var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(target.Headers);
                if (headers != null)
                {
                    foreach (var (key, value) in headers)
                        content.Headers.TryAddWithoutValidation(key, value);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Invalid headers JSON for webhook target {Name}", target.Name);
            }
        }

        // N3: use the named HttpClient registered in DI ("WebhookTest") instead of creating
        // a fresh one each call. The factory manages handler lifetime correctly.
        var httpClient = _httpClientFactory.CreateClient("WebhookTest");

        try
        {
            using var response = await httpClient.PostAsync(target.Url, content, ct);
            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Webhook test successful for target {Name}: HTTP {StatusCode}", target.Name, (int)response.StatusCode);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Webhook test failed for target {Name}: {Error}", target.Name, ex.Message);
            throw new InvalidOperationException($"Webhook test failed: {ex.Message}", ex);
        }
    }
}
