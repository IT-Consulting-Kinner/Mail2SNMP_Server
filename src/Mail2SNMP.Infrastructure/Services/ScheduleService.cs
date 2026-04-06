using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Mail2SNMP.Infrastructure.Services;

/// <summary>
/// CRUD implementation for polling schedules with eager-loading of the related Job.
/// </summary>
public class ScheduleService : IScheduleService
{
    private readonly Mail2SnmpDbContext _db;
    private readonly IAuditService _audit;

    public ScheduleService(Mail2SnmpDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>
    /// Returns all schedules ordered by name, eager-loading the related Job.
    /// </summary>
    public async Task<IReadOnlyList<Schedule>> GetAllAsync(CancellationToken ct = default)
        => await _db.Schedules.AsNoTracking().Include(s => s.Job).OrderBy(s => s.Name).ToListAsync(ct);

    /// <summary>
    /// Returns a single schedule by its identifier with the related Job included, or <c>null</c> if not found.
    /// </summary>
    public async Task<Schedule?> GetByIdAsync(int id, CancellationToken ct = default)
        => await _db.Schedules.AsNoTracking().Include(s => s.Job).FirstOrDefaultAsync(s => s.Id == id, ct);

    /// <summary>
    /// Creates a new polling schedule.
    /// </summary>
    public async Task<Schedule> CreateAsync(Schedule schedule, CancellationToken ct = default)
    {
        _db.Schedules.Add(schedule);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(Models.Enums.ActorType.System, "system", "Schedule.Created", "Schedule", schedule.Id.ToString(), ct: ct);
        return schedule;
    }

    /// <summary>
    /// Updates an existing schedule configuration.
    /// </summary>
    public async Task<Schedule> UpdateAsync(Schedule schedule, CancellationToken ct = default)
    {
        var existing = _db.ChangeTracker.Entries<Schedule>()
            .FirstOrDefault(e => e.Entity.Id == schedule.Id);
        if (existing != null)
            existing.CurrentValues.SetValues(schedule);
        else
            _db.Schedules.Update(schedule);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(Models.Enums.ActorType.System, "system", "Schedule.Updated", "Schedule", schedule.Id.ToString(), ct: ct);
        return schedule;
    }

    /// <summary>
    /// Deletes a schedule by its identifier.
    /// </summary>
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var schedule = await _db.Schedules.FindAsync(new object[] { id }, ct)
            ?? throw new KeyNotFoundException($"Schedule {id} not found.");
        _db.Schedules.Remove(schedule);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(Models.Enums.ActorType.System, "system", "Schedule.Deleted", "Schedule", id.ToString(), ct: ct);
    }

    /// <summary>
    /// Toggles the active state of a schedule between enabled and disabled.
    /// </summary>
    public async Task ToggleAsync(int id, CancellationToken ct = default)
    {
        var schedule = await _db.Schedules.FindAsync(new object[] { id }, ct)
            ?? throw new KeyNotFoundException($"Schedule {id} not found.");
        schedule.IsActive = !schedule.IsActive;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(Models.Enums.ActorType.System, "system",
            schedule.IsActive ? "Schedule.Activated" : "Schedule.Deactivated",
            "Schedule", id.ToString(), ct: ct);
    }
}
