using Mail2SNMP.Core.Services;

namespace Mail2SNMP.Tests.Core;

public class NotificationDedupCacheTests
{
    [Fact]
    public void IsDuplicate_FirstCall_ReturnsFalse()
    {
        var cache = new NotificationDedupCache();
        Assert.False(cache.IsDuplicate("target1", 1));
    }

    [Fact]
    public void IsDuplicate_SecondCallSameTarget_ReturnsTrue()
    {
        var cache = new NotificationDedupCache();
        cache.IsDuplicate("target1", 1);
        Assert.True(cache.IsDuplicate("target1", 1));
    }

    [Fact]
    public void IsDuplicate_DifferentTargetSameEvent_ReturnsFalse()
    {
        var cache = new NotificationDedupCache();
        cache.IsDuplicate("target1", 1);
        Assert.False(cache.IsDuplicate("target2", 1));
    }

    [Fact]
    public void IsDuplicate_SameTargetDifferentEvent_ReturnsFalse()
    {
        var cache = new NotificationDedupCache();
        cache.IsDuplicate("target1", 1);
        Assert.False(cache.IsDuplicate("target1", 2));
    }
}
