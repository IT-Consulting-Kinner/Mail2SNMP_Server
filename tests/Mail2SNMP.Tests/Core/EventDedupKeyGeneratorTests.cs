using Mail2SNMP.Core.Services;

namespace Mail2SNMP.Tests.Core;

public class EventDedupKeyGeneratorTests
{
    [Fact]
    public void Generate_SameInput_SameHash()
    {
        var hash1 = EventDedupKeyGenerator.Generate("msg-001", 1);
        var hash2 = EventDedupKeyGenerator.Generate("msg-001", 1);
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Generate_DifferentMessageId_DifferentHash()
    {
        var hash1 = EventDedupKeyGenerator.Generate("msg-001", 1);
        var hash2 = EventDedupKeyGenerator.Generate("msg-002", 1);
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Generate_DifferentMailbox_DifferentHash()
    {
        var hash1 = EventDedupKeyGenerator.Generate("msg-001", 1);
        var hash2 = EventDedupKeyGenerator.Generate("msg-001", 2);
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Generate_Returns64CharHex()
    {
        var hash = EventDedupKeyGenerator.Generate("test", 1);
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void GenerateFallback_SameInput_SameHash()
    {
        var time = new DateTime(2025, 3, 1, 12, 30, 45, DateTimeKind.Utc);
        var hash1 = EventDedupKeyGenerator.GenerateFallback("Subject", "from@test.com", time, 1);
        var hash2 = EventDedupKeyGenerator.GenerateFallback("Subject", "from@test.com", time, 1);
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void GenerateFallback_TruncatesToMinute()
    {
        var time1 = new DateTime(2025, 3, 1, 12, 30, 15, DateTimeKind.Utc);
        var time2 = new DateTime(2025, 3, 1, 12, 30, 45, DateTimeKind.Utc);
        var hash1 = EventDedupKeyGenerator.GenerateFallback("Subject", "from@test.com", time1, 1);
        var hash2 = EventDedupKeyGenerator.GenerateFallback("Subject", "from@test.com", time2, 1);
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void GenerateFallback_DifferentMinute_DifferentHash()
    {
        var time1 = new DateTime(2025, 3, 1, 12, 30, 0, DateTimeKind.Utc);
        var time2 = new DateTime(2025, 3, 1, 12, 31, 0, DateTimeKind.Utc);
        var hash1 = EventDedupKeyGenerator.GenerateFallback("Subject", "from@test.com", time1, 1);
        var hash2 = EventDedupKeyGenerator.GenerateFallback("Subject", "from@test.com", time2, 1);
        Assert.NotEqual(hash1, hash2);
    }
}
