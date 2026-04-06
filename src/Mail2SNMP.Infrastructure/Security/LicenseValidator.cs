using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Mail2SNMP.Infrastructure.Security;

/// <summary>
/// Validates and parses JWT-based license tokens. Falls back to Community edition defaults if no valid license is found.
/// </summary>
public class LicenseValidator : ILicenseProvider
{
    private LicenseInfo _current;
    private readonly string? _licenseFilePath;
    private readonly ILogger<LicenseValidator> _logger;
    private readonly bool _allowUnsigned;

    private static readonly LicenseInfo CommunityDefault = new()
    {
        Edition = LicenseEdition.Community,
        MaxMailboxes = 3,
        MaxJobs = 5,
        MaxWorkerInstances = 1,
        Features = Array.Empty<string>()
    };

    /// <param name="logger">Logger used for license loading diagnostics and warnings.</param>
    /// <param name="licenseFilePath">
    /// Optional explicit path to <c>license.key</c>. When <c>null</c>, the validator
    /// searches the standard locations (next to the executable, then ProgramData).
    /// </param>
    /// <param name="allowUnsigned">
    /// When true, unsigned (alg=none) license tokens are accepted with a warning.
    /// Must only be true in Development environments. Production MUST use RS256 (v5.8).
    /// </param>
    public LicenseValidator(ILogger<LicenseValidator> logger, string? licenseFilePath = null, bool allowUnsigned = false)
    {
        _logger = logger;
        _allowUnsigned = allowUnsigned;
        _licenseFilePath = licenseFilePath ?? FindLicenseFile();
        _current = LoadLicense();
    }

    /// <summary>
    /// Gets the currently loaded license information.
    /// </summary>
    public LicenseInfo Current => _current;

    /// <summary>
    /// Returns <c>true</c> if the current license is an Enterprise edition.
    /// </summary>
    public bool IsEnterprise() => _current.Edition == LicenseEdition.Enterprise;

    /// <summary>
    /// Returns <c>true</c> if the license is Enterprise and includes the specified feature.
    /// </summary>
    public bool HasFeature(string featureName) =>
        IsEnterprise() && _current.Features.Contains(featureName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the numeric limit for the given constraint name (e.g. "maxmailboxes", "maxjobs").
    /// </summary>
    public int GetLimit(string limitName) => limitName.ToLowerInvariant() switch
    {
        "maxmailboxes" => _current.MaxMailboxes,
        "maxjobs" => _current.MaxJobs,
        "maxworkerinstances" => _current.MaxWorkerInstances,
        _ => 0
    };

    /// <summary>
    /// Reloads the license from the file or environment variable, replacing the cached value.
    /// </summary>
    public Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        _current = LoadLicense();
        _logger.LogInformation("License reloaded. Edition: {Edition}", _current.Edition);
        return Task.CompletedTask;
    }

    private LicenseInfo LoadLicense()
    {
        // Check env var first
        var envLicense = Environment.GetEnvironmentVariable("MAIL2SNMP_LICENSE");
        if (!string.IsNullOrEmpty(envLicense))
            return ParseLicenseToken(envLicense);

        if (string.IsNullOrEmpty(_licenseFilePath) || !File.Exists(_licenseFilePath))
        {
            _logger.LogInformation("No license file found. Running as Community Edition.");
            return CommunityDefault;
        }

        try
        {
            var token = File.ReadAllText(_licenseFilePath).Trim();
            return ParseLicenseToken(token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load license file. Falling back to Community Edition.");
            return CommunityDefault;
        }
    }

    /// <summary>
    /// Embedded RSA-2048 public key for RS256 license signature verification (PEM format).
    /// The corresponding private key is used by the license-issuing tool to sign tokens.
    /// </summary>
    private static readonly string LicensePublicKeyPem = @"-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAtN7x3f0rYUHVUhTWP5XD
11EJ+RucZbdS8sY/FutJuT3t2llqtOet6uOTAkx4kvqFDQDGxB2qWeacQQ5Rh0vu
ShIMFZXpyE+/+eWmuX2n39oxYSirs7BXnGGljRLeQPzMCdNSjCXdS3xEZAwdto5x
lCh0y+E56nYVJ9t4Kmq/Nzk1RXb0NRw413qwM/JvVlK99Vh7BapuIL5SyEFMPMSl
mUFHqzh1LrrS7Vfp/I35WR0v6xPhgKoSCR1TFYeivPMnLdBGfeH61upTnZolRbIv
M9Cz5iLTLDSyTEQT1Pn/qOf9OersfjgHpVq7tE8UmLA+exGNDeeOjnOXuw1TgTUx
4wIDAQAB
-----END PUBLIC KEY-----";

    private LicenseInfo ParseLicenseToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token))
            {
                _logger.LogWarning("Invalid license token format. Falling back to Community Edition.");
                return CommunityDefault;
            }

            // Read the token header to check the signing algorithm
            var jwt = handler.ReadJwtToken(token);
            var algorithm = jwt.Header.Alg;

            if (string.Equals(algorithm, "RS256", StringComparison.OrdinalIgnoreCase))
            {
                // RS256-signed token: validate signature with the embedded public key
                var rsa = RSA.Create();
                rsa.ImportFromPem(LicensePublicKeyPem);
                var rsaKey = new RsaSecurityKey(rsa);

                var validationParams = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = rsaKey,
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(5)
                };

                try
                {
                    handler.ValidateToken(token, validationParams, out _);
                    _logger.LogInformation("License token RS256 signature verified successfully");
                }
                catch (SecurityTokenInvalidSignatureException)
                {
                    _logger.LogWarning(
                        "License token has an INVALID RS256 signature. Rejecting. " +
                        "Ensure the license was signed with the correct private key.");
                    return CommunityDefault;
                }
                catch (SecurityTokenExpiredException)
                {
                    _logger.LogWarning("License token has expired according to signature validation.");
                    return CommunityDefault;
                }
            }
            else if (string.Equals(algorithm, "none", StringComparison.OrdinalIgnoreCase)
                     || string.IsNullOrEmpty(algorithm))
            {
                // Unsigned token: only accepted in Development when explicitly allowed (v5.8)
                if (!_allowUnsigned)
                {
                    _logger.LogWarning(
                        "License token is UNSIGNED (alg={Algorithm}). " +
                        "Unsigned licenses are rejected in Production. Use an RS256-signed license. " +
                        "Falling back to Community Edition.", algorithm ?? "empty");
                    return CommunityDefault;
                }

                _logger.LogWarning(
                    "License token is UNSIGNED (alg={Algorithm}). " +
                    "Accepted because allowUnsigned=true (Development environment). " +
                    "Production deployments MUST use RS256-signed licenses.", algorithm ?? "empty");
            }
            else
            {
                // Unsupported algorithm — reject
                _logger.LogWarning(
                    "License token uses unsupported algorithm '{Algorithm}'. " +
                    "Only RS256 is supported. Falling back to Community Edition.", algorithm);
                return CommunityDefault;
            }

            var edition = jwt.Claims.FirstOrDefault(c => c.Type == "edition")?.Value;
            if (!Enum.TryParse<LicenseEdition>(edition, true, out var licenseEdition))
                licenseEdition = LicenseEdition.Community;

            var expiresUtc = jwt.ValidTo;
            if (expiresUtc != DateTime.MinValue && expiresUtc < DateTime.UtcNow)
            {
                _logger.LogWarning("License expired on {ExpiresUtc}. Falling back to Community Edition.", expiresUtc);
                return CommunityDefault;
            }

            var info = new LicenseInfo
            {
                LicenseId = jwt.Claims.FirstOrDefault(c => c.Type == "license_id")?.Value ?? "",
                CustomerName = jwt.Claims.FirstOrDefault(c => c.Type == "customer_name")?.Value ?? "",
                Edition = licenseEdition,
                ExpiresUtc = expiresUtc == DateTime.MinValue ? null : expiresUtc,
                MaxMailboxes = int.TryParse(jwt.Claims.FirstOrDefault(c => c.Type == "max_mailboxes")?.Value, out var mm) ? mm : (licenseEdition == LicenseEdition.Enterprise ? int.MaxValue : 3),
                MaxJobs = int.TryParse(jwt.Claims.FirstOrDefault(c => c.Type == "max_jobs")?.Value, out var mj) ? mj : (licenseEdition == LicenseEdition.Enterprise ? int.MaxValue : 5),
                MaxWorkerInstances = int.TryParse(jwt.Claims.FirstOrDefault(c => c.Type == "max_workers")?.Value, out var mw) ? mw : (licenseEdition == LicenseEdition.Enterprise ? int.MaxValue : 1),
                Features = jwt.Claims.Where(c => c.Type == "feature").Select(c => c.Value).ToArray()
            };

            _logger.LogInformation("License loaded: {Edition} for {Customer}, expires {Expires}",
                info.Edition, info.CustomerName, info.ExpiresUtc?.ToString("O") ?? "never");

            return info;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse license token. Falling back to Community Edition.");
            return CommunityDefault;
        }
    }

    private static string? FindLicenseFile()
    {
        var licenseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "IT-Consulting Kinner", "Mail2SNMP_Server", "License");
        var candidates = new[]
        {
            Path.Combine(licenseDir, "license.key"),
            Path.Combine(AppContext.BaseDirectory, "license.key")
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
