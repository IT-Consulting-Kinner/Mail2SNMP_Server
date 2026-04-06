using Mail2SNMP.Infrastructure.Security;

namespace Mail2SNMP.Tests.Infrastructure;

/// <summary>
/// I13: Tests for the API key auth handler. The full handler requires an
/// HttpContext and DI plumbing, so we focus on the deterministic, security-
/// critical pure helpers (HashKey) which is what an attacker would target.
/// </summary>
public class ApiKeyAuthenticationHandlerTests
{
    [Fact]
    public void HashKey_ProducesDeterministicHexDigest()
    {
        var a = ApiKeyAuthenticationHandler.HashKey("m2s_test_value_12345");
        var b = ApiKeyAuthenticationHandler.HashKey("m2s_test_value_12345");
        Assert.Equal(a, b);
    }

    [Fact]
    public void HashKey_Returns64HexCharacters()
    {
        var hash = ApiKeyAuthenticationHandler.HashKey("any-value");
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void HashKey_DifferentInputsProduceDifferentDigests()
    {
        var a = ApiKeyAuthenticationHandler.HashKey("alpha");
        var b = ApiKeyAuthenticationHandler.HashKey("beta");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void HashKey_NeverContainsPlaintext()
    {
        const string plaintext = "secret-key-do-not-leak";
        var hash = ApiKeyAuthenticationHandler.HashKey(plaintext);
        Assert.DoesNotContain(plaintext, hash);
    }

    [Fact]
    public void SchemeName_IsApiKey()
    {
        // Sanity check — endpoint policies and the API/Web registration both depend
        // on this constant being exactly "ApiKey".
        Assert.Equal("ApiKey", ApiKeyAuthenticationHandler.SchemeName);
        Assert.Equal("X-Api-Key", ApiKeyAuthenticationHandler.HeaderName);
    }
}
