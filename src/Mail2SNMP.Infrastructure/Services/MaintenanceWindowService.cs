using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Mail2SNMP.Infrastructure.Services;

/// <summary>
/// Manages maintenance windows and provides runtime checks for alert suppression.
/// </summary>
public class MaintenanceWindowService : IMaintenanceWindowService
{
    private readonly Mail2SnmpDbContext _db;
    private readonly IAuditService _audit;

    public MaintenanceWindowService(Mail2SnmpDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>
    /// Returns all maintenance windows ordered by start time descending.
    /// </summary>
    public async Task<IReadOnlyList<MaintenanceWindow>> GetAllAsync(CancellationToken ct = default)
        => await _db.MaintenanceWindows.AsNoTracking().OrderByDescending(m => m.StartUtc).ToListAsync(ct);

    /// <summary>
    /// Returns a single maintenance window by its identifier, or <c>null</c> if not found.
    /// </summary>
    public async Task<MaintenanceWindow?> GetByIdAsync(int id, CancellationToken ct = default)
        => await _db.MaintenanceWindows.FindAsync(new object[] { id }, ct);

    /// <summary>
    /// Creates a new maintenance window.
    /// </summary>
    public async Task<MaintenanceWindow> CreateAsync(MaintenanceWindow window, CancellationToken ct = default)
    {
        _db.MaintenanceWindows.Add(window);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(Models.Enums.ActorType.User, window.CreatedBy, "MaintenanceWindow.Created", "MaintenanceWindow", window.Id.ToString(), ct: ct);
        return window;
    }

    /// <summary>
    /// Deletes a maintenance window by its identifier.
    /// </summary>
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var window = await _db.MaintenanceWindows.FindAsync(new object[] { id }, ct)
            ?? throw new KeyNotFoundException($"Maintenance window {id} not found.");
        _db.MaintenanceWindows.Remove(window);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Checks whether any active maintenance window currently covers the given job or all jobs.
    /// </summary>
    public async Task<bool> IsInMaintenanceAsync(int? jobId = null, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.MaintenanceWindows.AnyAsync(m =>
            m.IsActive && m.StartUtc <= now && m.EndUtc >= now &&
            (m.Scope == "All" || (jobId.HasValue && m.Scope.Contains(jobId.Value.ToString()))),
            ct);
    }
}
