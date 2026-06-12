using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mail2SNMP.Infrastructure.Data;

/// <summary>
/// Design-time factory for creating the DbContext during EF Core Migrations tooling (dotnet ef).
/// Uses SQLite with a default connection string — the actual connection string is supplied at runtime.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<Mail2SnmpDbContext>
{
    /// <summary>
    /// Creates a <see cref="Mail2SnmpDbContext"/> configured for the SQLite provider, for use by the
    /// EF Core design-time tooling (e.g. <c>dotnet ef migrations add</c>). This context is never used at
    /// runtime; the production connection string and provider are configured separately during host startup.
    /// </summary>
    /// <param name="args">Command-line arguments passed by the EF Core tooling (unused).</param>
    /// <returns>A context bound to a local design-time SQLite database file.</returns>
    public Mail2SnmpDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<Mail2SnmpDbContext>();
        optionsBuilder.UseSqlite("Data Source=mail2snmp-design.db");
        return new Mail2SnmpDbContext(optionsBuilder.Options);
    }
}
