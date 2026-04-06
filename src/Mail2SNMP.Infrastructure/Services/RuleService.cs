using Mail2SNMP.Core.Exceptions;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Mail2SNMP.Infrastructure.Services;

/// <summary>
/// CRUD implementation for email parsing rules.
/// </summary>
public class RuleService : IRuleService
{
    private readonly Mail2SnmpDbContext _db;
    private readonly IAuditService _audit;

    public RuleService(Mail2SnmpDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>
    /// Returns all rules ordered by priority.
    /// </summary>
    public async Task<IReadOnlyList<Rule>> GetAllAsync(CancellationToken ct = default)
        => await _db.Rules.AsNoTracking().OrderBy(r => r.Priority).ToListAsync(ct);

    /// <summary>
    /// Returns a single rule by its identifier, or <c>null</c> if not found.
    /// </summary>
    public async Task<Rule?> GetByIdAsync(int id, CancellationToken ct = default)
        => await _db.Rules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);

    /// <summary>
    /// Creates a new email parsing rule.
    /// </summary>
    public async Task<Rule> CreateAsync(Rule rule, CancellationToken ct = default)
    {
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(Models.Enums.ActorType.System, "system", "Rule.Created", "Rule", rule.Id.ToString(), ct: ct);
        return rule;
    }

    /// <summary>
    /// Updates an existing rule configuration.
    /// </summary>
    public async Task<Rule> UpdateAsync(Rule rule, CancellationToken ct = default)
    {
        var existing = _db.ChangeTracker.Entries<Rule>()
            .FirstOrDefault(e => e.Entity.Id == rule.Id);
        if (existing != null)
            existing.CurrentValues.SetValues(rule);
        else
            _db.Rules.Update(rule);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(Models.Enums.ActorType.System, "system", "Rule.Updated", "Rule", rule.Id.ToString(), ct: ct);
        return rule;
    }

    /// <summary>
    /// Deletes a rule by its identifier. Throws <see cref="DependencyException"/>
    /// if any job still references this rule.
    /// </summary>
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var rule = await _db.Rules.FindAsync(new object[] { id }, ct)
            ?? throw new KeyNotFoundException($"Rule {id} not found.");

        var referencingJob = await _db.Jobs.FirstOrDefaultAsync(j => j.RuleId == id, ct);
        if (referencingJob != null)
            throw new DependencyException($"Rule '{rule.Name}' cannot be deleted — it is used by Job '{referencingJob.Name}'.");

        _db.Rules.Remove(rule);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(Models.Enums.ActorType.System, "system", "Rule.Deleted", "Rule", id.ToString(), ct: ct);
    }
}
