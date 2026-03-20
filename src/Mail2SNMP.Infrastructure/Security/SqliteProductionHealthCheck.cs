using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Mail2SNMP.Infrastructure.Security;

/// <summary>
/// Health check that reports Degraded when SQLite is used outside of Development (v5.8).
/// SQLite is only suitable for dev/demo. Production must use SQL Server.
/// </summary>
public class SqliteProductionHealthCheck : IHealthCheck
{
    private readonly string _dbProvider;
    private readonly bool _isDevelopment;

    public SqliteProductionHealthCheck(IConfiguration configuration)
    {
        _dbProvider = configuration["Database:Provider"] ?? "Sqlite";
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                  ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                  ?? "Production";
        _isDevelopment = env.Equals("Development", StringComparison.OrdinalIgnoreCase);
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!_dbProvider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(HealthCheckResult.Healthy("Using SQL Server."));

        if (_isDevelopment)
            return Task.FromResult(HealthCheckResult.Healthy("SQLite in Development mode (acceptable)."));

        return Task.FromResult(HealthCheckResult.Degraded(
            "SQLite is not recommended for production. " +
            "Use SQL Server for clustering, concurrent access, and data safety. " +
            "Set Database:Provider to 'SqlServer' in appsettings.json."));
    }
}
