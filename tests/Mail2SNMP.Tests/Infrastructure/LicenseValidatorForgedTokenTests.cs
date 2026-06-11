using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Mail2SNMP.Infrastructure.Security;
using Mail2SNMP.Models.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace Mail2SNMP.Tests.Infrastructure;

/// <summary>
/// Peer-review: the embedded-public-key RS256 signature path is the linchpin of
/// the licensing model — without these tests, a regression that accidentally
/// trusts unsigned or foreign-signed tokens would silently unlock Enterprise.
/// These tests forge tokens an attacker would actually try and assert the
/// validator falls back to Community.
/// </summary>
public class LicenseValidatorForgedTokenTests
{
    private static LicenseEdition EditionFromToken(string token, bool allowUnsigned = false)
    {
        var path = Path.Combine(Path.GetTempPath(), $"m2s-license-{Guid.NewGuid():N}.key");
        try
        {
            File.WriteAllText(path, token);
            var validator = new LicenseValidator(NullLogger<LicenseValidator>.Instance, path, allowUnsigned);
            return validator.Current.Edition;
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public void AlgNoneToken_ClaimingEnterprise_RejectedInProduction()
    {
        // Hand-craft an unsigned "alg":"none" JWT that claims Enterprise.
        // This is the classic JWT downgrade attack.
        var header = Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}"));
        var payload = Base64Url(Encoding.UTF8.GetBytes("{\"edition\":\"Enterprise\"}"));
        var token = $"{header}.{payload}."; // empty signature segment

        // allowUnsigned=false (Production default) → must be rejected.
        Assert.Equal(LicenseEdition.Community, EditionFromToken(token, allowUnsigned: false));
    }

    [Fact]
    public void Rs256Token_SignedWithForeignKey_RejectedAsCommunity()
    {
        // Sign an Enterprise-claiming token with a freshly generated RSA key that
        // is NOT the embedded license public key — signature validation must fail.
        using var rsa = RSA.Create(2048);
        var creds = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256);
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateEncodedJwt(new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object> { ["edition"] = "Enterprise" },
            Expires = DateTime.UtcNow.AddYears(1),
            SigningCredentials = creds
        });

        Assert.Equal(LicenseEdition.Community, EditionFromToken(token));
    }

    [Fact]
    public void GarbageToken_RejectedAsCommunity()
    {
        Assert.Equal(LicenseEdition.Community, EditionFromToken("this.is.not-a-jwt"));
    }
}
