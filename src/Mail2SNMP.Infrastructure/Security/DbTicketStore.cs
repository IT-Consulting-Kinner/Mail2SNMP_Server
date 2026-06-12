using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Infrastructure.Security;

/// <summary>
/// Database-backed implementation of <see cref="ITicketStore"/> for server-side session storage.
/// Keeps authentication cookies small by storing the full ticket in the database
/// and only placing a session identifier in the cookie.
/// </summary>
public class DbTicketStore : ITicketStore
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DbTicketStore> _logger;

    /// <summary>
    /// Initializes the ticket store.
    /// </summary>
    /// <param name="scopeFactory">
    /// Factory used to create a fresh DI scope (and thus a fresh <see cref="Mail2SnmpDbContext"/>) per operation,
    /// since this store is a singleton consumed by the cookie middleware while the context is scoped.
    /// </param>
    /// <param name="logger">Logger for ticket lifecycle events (store, renew, expire, remove).</param>
    public DbTicketStore(IServiceScopeFactory scopeFactory, ILogger<DbTicketStore> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Stores a new authentication ticket and returns the session key for the cookie.
    /// </summary>
    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Guid.NewGuid().ToString();
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Mail2SnmpDbContext>();

        var entity = new AuthTicket
        {
            Id = key,
            Value = TicketSerializer.Default.Serialize(ticket),
            LastActivity = DateTime.UtcNow,
            ExpiresUtc = ticket.Properties.ExpiresUtc?.UtcDateTime
        };

        db.AuthTickets.Add(entity);
        await db.SaveChangesAsync();
        _logger.LogDebug("Stored auth ticket {Key}", key);
        return key;
    }

    /// <summary>
    /// Renews an existing ticket (updates the serialized data and expiration).
    /// </summary>
    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Mail2SnmpDbContext>();

        var entity = await db.AuthTickets.FindAsync(key);
        if (entity is null)
        {
            _logger.LogDebug("Auth ticket {Key} not found for renewal, creating new entry", key);
            entity = new AuthTicket { Id = key };
            db.AuthTickets.Add(entity);
        }

        entity.Value = TicketSerializer.Default.Serialize(ticket);
        entity.LastActivity = DateTime.UtcNow;
        entity.ExpiresUtc = ticket.Properties.ExpiresUtc?.UtcDateTime;

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Retrieves a ticket by its session key. Returns <c>null</c> if not found or expired.
    /// </summary>
    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Mail2SnmpDbContext>();

        var entity = await db.AuthTickets.FindAsync(key);
        if (entity is null)
            return null;

        // Remove expired tickets on access
        if (entity.ExpiresUtc.HasValue && entity.ExpiresUtc.Value < DateTime.UtcNow)
        {
            db.AuthTickets.Remove(entity);
            await db.SaveChangesAsync();
            _logger.LogDebug("Auth ticket {Key} expired and removed", key);
            return null;
        }

        entity.LastActivity = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return TicketSerializer.Default.Deserialize(entity.Value);
    }

    /// <summary>
    /// Removes a ticket from the store (logout or session invalidation).
    /// </summary>
    public async Task RemoveAsync(string key)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Mail2SnmpDbContext>();

        var entity = await db.AuthTickets.FindAsync(key);
        if (entity is not null)
        {
            db.AuthTickets.Remove(entity);
            await db.SaveChangesAsync();
            _logger.LogDebug("Auth ticket {Key} removed", key);
        }
    }
}
