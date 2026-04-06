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
        => await _db.MaintenanceWindows.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);

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
    /// The window <c>Scope</c> field is either the literal string <c>"All"</c> or a comma-separated
    /// list of job identifiers (e.g. <c>"5,12,103"</c>). The previous implementation used a naive
    /// <c>Contains()</c> match, which incorrectly matched job 5 within "15" or "25". This version
    /// loads the candidate windows and parses the scope explicitly to compare full IDs.
    /// </summary>
    public async Task<bool> IsInMaintenanceAsync(int? jobId = null, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Quick path: any "All" window currently active
        var anyAll = await _db.MaintenanceWindows.AnyAsync(m =>
            m.IsActive && m.StartUtc <= now && m.EndUtc >= now && m.Scope == "All", ct);
        if (anyAll || !jobId.HasValue)
            return anyAll;

        // Load active per-scope windows and parse the comma-separated job list in memory
        // (entity counts are bounded by IsActive, so this is O(active windows)).
        var candidates = await _db.MaintenanceWindows
            .Where(m => m.IsActive && m.StartUtc <= now && m.EndUtc >= now && m.Scope != "All")
            .Select(m => m.Scope)
            .ToListAsync(ct);

        var target = jobId.Value;
        foreach (var scope in candidates)
        {
            if (string.IsNullOrWhiteSpace(scope))
                continue;
            foreach (var part in scope.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part, out var id) && id == target)
                    return true;
            }
        }
        return false;
    }
}
