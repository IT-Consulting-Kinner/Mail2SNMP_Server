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

        // G4: load every active window once (bounded by IsActive). For each window we
        // evaluate three modes:
        //   1. Fixed window: StartUtc <= now <= EndUtc (legacy behaviour).
        //   2. Recurring window: RecurringCron set — find the most recent cron occurrence
        //      and treat (occurrence, occurrence + originalDuration) as the active window.
        // The original duration is taken from EndUtc - StartUtc so the operator only has
        // to define the duration once via the start/end time pickers.
        var allActive = await _db.MaintenanceWindows
            .Where(m => m.IsActive)
            .ToListAsync(ct);

        bool MatchesScope(string scope)
        {
            if (string.IsNullOrWhiteSpace(scope) || scope.Equals("All", StringComparison.OrdinalIgnoreCase))
                return true;
            if (!jobId.HasValue) return false;
            var target = jobId.Value;
            foreach (var part in scope.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(part, out var id) && id == target) return true;
            return false;
        }

        foreach (var w in allActive)
        {
            if (!MatchesScope(w.Scope)) continue;

            // 1) fixed window
            if (w.StartUtc <= now && w.EndUtc >= now)
                return true;

            // 2) recurring window
            if (!string.IsNullOrWhiteSpace(w.RecurringCron))
            {
                try
                {
                    var cron = Cronos.CronExpression.Parse(w.RecurringCron);
                    var duration = w.EndUtc - w.StartUtc;
                    if (duration <= TimeSpan.Zero) duration = TimeSpan.FromHours(1); // fallback
                    // GetOccurrences returns all occurrences in [from, to). We look back
                    // from a window equal to the duration, so a currently-active recurrence
                    // is captured.
                    var lookback = now - duration;
                    var occurrence = cron.GetOccurrences(lookback, now, fromInclusive: true, toInclusive: true)
                                         .Cast<DateTime?>().LastOrDefault();
                    if (occurrence is DateTime occ)
                    {
                        var winEnd = occ + duration;
                        if (occ <= now && now <= winEnd) return true;
                    }
                }
                catch (Cronos.CronFormatException)
                {
                    // Bad cron expression — ignore this window, do not crash the check.
                }
            }
        }
        return false;
    }
}
