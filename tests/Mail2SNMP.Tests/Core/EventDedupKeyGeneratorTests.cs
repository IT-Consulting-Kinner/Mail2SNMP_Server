using Mail2SNMP.Core.Services;

namespace Mail2SNMP.Tests.Core;

/// <summary>
/// H-3: the dedup key must identify the ALERT, not the message. Repeated mails about
/// the same condition have to share a key so they collapse into one event with a
/// rising hit count; only a genuinely different alert (or a different job) may differ.
/// </summary>
public class EventDedupKeyGeneratorTests
{
    [Fact]
    public void Generate_SameSubjectAndSender_SameHash()
    {
        var a = EventDedupKeyGenerator.Generate("Disk full on srv01", "monitor@corp.com", 1);
        var b = EventDedupKeyGenerator.Generate("Disk full on srv01", "monitor@corp.com", 1);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Generate_IsIndependentOfMessageIdAndTime()
    {
        // The core regression: two successive alert mails from the same monitoring
        // system carry different Message-IDs and arrive at different times. Under the
        // old Message-ID-based key they produced different hashes, so the dedup window
        // could never collapse them and HitCount never left 1.
        var first = EventDedupKeyGenerator.Generate("Disk full on srv01", "monitor@corp.com", 7);
        var later = EventDedupKeyGenerator.Generate("Disk full on srv01", "monitor@corp.com", 7);
        Assert.Equal(first, later);
    }

    [Fact]
    public void Generate_DifferentSubject_DifferentHash()
    {
        var a = EventDedupKeyGenerator.Generate("Disk full on srv01", "monitor@corp.com", 1);
        var b = EventDedupKeyGenerator.Generate("Disk full on srv02", "monitor@corp.com", 1);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Generate_DifferentSender_DifferentHash()
    {
        var a = EventDedupKeyGenerator.Generate("Disk full", "monitor-a@corp.com", 1);
        var b = EventDedupKeyGenerator.Generate("Disk full", "monitor-b@corp.com", 1);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Generate_DifferentJob_DifferentHash()
    {
        // Dedup is scoped per job: two jobs watching the same mailbox must not
        // suppress each other's events.
        var a = EventDedupKeyGenerator.Generate("Disk full", "monitor@corp.com", 1);
        var b = EventDedupKeyGenerator.Generate("Disk full", "monitor@corp.com", 2);
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData("Disk full on srv01", "  Disk full on srv01  ")]   // padding
    [InlineData("Disk full on srv01", "disk FULL on SRV01")]        // casing
    [InlineData("Disk full on srv01", "Disk   full\ton srv01")]     // whitespace runs
    public void Generate_NormalizesCosmeticDifferences(string a, string b)
    {
        Assert.Equal(
            EventDedupKeyGenerator.Generate(a, "monitor@corp.com", 1),
            EventDedupKeyGenerator.Generate(b, "monitor@corp.com", 1));
    }

    [Fact]
    public void Generate_HandlesNullSubjectAndSender()
    {
        var a = EventDedupKeyGenerator.Generate(null, null, 1);
        var b = EventDedupKeyGenerator.Generate(null, null, 1);
        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);
    }

    [Fact]
    public void Generate_Returns64CharLowercaseHex()
    {
        var hash = EventDedupKeyGenerator.Generate("test", "a@b.c", 1);
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }
}
