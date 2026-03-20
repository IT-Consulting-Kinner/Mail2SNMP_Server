using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mail2SNMP.Infrastructure.Data;

/// <summary>
/// Design-time factory for creating the DbContext during EF Core Migrations tooling (dotnet ef).
/// Uses SQLite with a default connection string — the actual connection string is supplied at runtime.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<Mail2SnmpDbContext>
{
    public Mail2SnmpDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<Mail2SnmpDbContext>();
        optionsBuilder.UseSqlite("Data Source=mail2snmp-design.db");
        return new Mail2SnmpDbContext(optionsBuilder.Options);
    }
}
