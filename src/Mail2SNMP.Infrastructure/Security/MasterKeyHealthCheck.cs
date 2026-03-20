using Mail2SNMP.Core.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Mail2SNMP.Infrastructure.Security;

/// <summary>
/// Health check that verifies the master encryption key is functional.
/// Reports Unhealthy if probe-decrypt fails, which blocks /health/ready (v5.8).
/// </summary>
public class MasterKeyHealthCheck : IHealthCheck
{
    private readonly ICredentialEncryptor _encryptor;

    public MasterKeyHealthCheck(ICredentialEncryptor encryptor)
    {
        _encryptor = encryptor;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var probe = _encryptor.Encrypt("health-check-probe");
            if (_encryptor.TryDecrypt(probe, out var result) && result == "health-check-probe")
                return Task.FromResult(HealthCheckResult.Healthy("Master key encryption is operational."));

            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Master key probe-decrypt returned incorrect result. The encryption subsystem is broken."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Master key probe-decrypt failed. Check the master key configuration.", ex));
        }
    }
}
