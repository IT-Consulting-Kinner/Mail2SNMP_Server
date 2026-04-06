using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Mail2SNMP.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mail2SNMP.Infrastructure.Security;

/// <summary>
/// G6: Authentication handler that validates an API key from the X-Api-Key header.
/// On success the request is signed in with the scheme name "ApiKey" and carries
/// claims for the key name plus one Claim per scope (so role-style policies work).
/// </summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    private readonly Mail2SnmpDbContext _db;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        Mail2SnmpDbContext db) : base(options, logger, encoder)
    {
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var headerValue))
            return AuthenticateResult.NoResult();

        var presented = headerValue.ToString();
        if (string.IsNullOrWhiteSpace(presented))
            return AuthenticateResult.Fail("Empty API key");

        var hash = HashKey(presented);

        // Lookup by hash (unique index). Update LastUsedUtc best-effort.
        var key = await _db.ApiKeys.FirstOrDefaultAsync(k => k.KeyHash == hash);
        if (key == null || !key.IsActive)
            return AuthenticateResult.Fail("Invalid API key");

        if (key.ExpiresUtc.HasValue && key.ExpiresUtc.Value < DateTime.UtcNow)
            return AuthenticateResult.Fail("API key expired");

        try
        {
            key.LastUsedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        catch
        {
            // Don't fail authentication just because we couldn't update the timestamp.
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, $"apikey:{key.Id}"),
            new(ClaimTypes.Name, key.Name),
            new("ApiKeyId", key.Id.ToString())
        };
        foreach (var scope in (key.Scopes ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            claims.Add(new Claim("scope", scope));

        // Map well-known scopes to ASP.NET roles so existing [Authorize(Policy="Operator")]
        // gates accept properly-scoped API keys.
        var scopes = (key.Scopes ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (scopes.Contains("admin", StringComparer.OrdinalIgnoreCase))
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        if (scopes.Contains("write", StringComparer.OrdinalIgnoreCase) || scopes.Contains("admin", StringComparer.OrdinalIgnoreCase))
            claims.Add(new Claim(ClaimTypes.Role, "Operator"));
        // Read is everyone authenticated
        claims.Add(new Claim(ClaimTypes.Role, "ReadOnly"));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    /// <summary>
    /// SHA-256 hex digest of the key. Same algorithm is used at creation time so the
    /// stored hash and the lookup hash always match exactly.
    /// </summary>
    public static string HashKey(string plaintext)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
