using Mail2SNMP.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mail2SNMP.Tests.Core;

public class FloodProtectionServiceTests
{
    private readonly FloodProtectionService _service = new(NullLogger<FloodProtectionService>.Instance);

    [Fact]
    public void IsRateLimited_UnderLimit_ReturnsFalse()
    {
        Assert.False(_service.IsRateLimited("target1", 10));
    }

    [Fact]
    public void IsRateLimited_AtLimit_ReturnsTrue()
    {
        for (int i = 0; i < 5; i++)
            _service.IsRateLimited("target2", 5);

        Assert.True(_service.IsRateLimited("target2", 5));
    }

    [Fact]
    public void IsRateLimited_DifferentTargets_IndependentCounters()
    {
        for (int i = 0; i < 5; i++)
            _service.IsRateLimited("targetA", 5);

        // targetA is at limit
        Assert.True(_service.IsRateLimited("targetA", 5));
        // targetB is not
        Assert.False(_service.IsRateLimited("targetB", 5));
    }

    [Fact]
    public void IsEventRateLimited_UnderLimit_ReturnsFalse()
    {
        Assert.False(_service.IsEventRateLimited(1, 50));
    }

    [Fact]
    public void IsEventRateLimited_AtLimit_ReturnsTrue()
    {
        for (int i = 0; i < 50; i++)
            _service.IsEventRateLimited(99, 50);

        Assert.True(_service.IsEventRateLimited(99, 50));
    }
}
