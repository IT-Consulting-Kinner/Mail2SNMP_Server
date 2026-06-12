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

    /// <summary>
    /// Captures the configured database provider and resolves whether the host is running in the
    /// Development environment (from <c>ASPNETCORE_ENVIRONMENT</c>/<c>DOTNET_ENVIRONMENT</c>, defaulting
    /// to Production when unset).
    /// </summary>
    /// <param name="configuration">Configuration read for the <c>Database:Provider</c> value (defaults to "Sqlite").</param>
    public SqliteProductionHealthCheck(IConfiguration configuration)
    {
        _dbProvider = configuration["Database:Provider"] ?? "Sqlite";
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                  ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                  ?? "Production";
        _isDevelopment = env.Equals("Development", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Evaluates whether the active database provider is appropriate for the environment.
    /// </summary>
    /// <param name="context">Health-check context supplied by the health-check framework.</param>
    /// <param name="cancellationToken">Unused; the check performs no I/O.</param>
    /// <returns>
    /// <see cref="HealthCheckResult.Healthy(string, System.Collections.Generic.IReadOnlyDictionary{string, object})"/>
    /// when SQL Server is configured, or when SQLite is used in Development; otherwise
    /// <see cref="HealthCheckResult.Degraded(string, System.Exception, System.Collections.Generic.IReadOnlyDictionary{string, object})"/>,
    /// because SQLite is unsuitable for production clustering, concurrent access, and data safety.
    /// </returns>
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
