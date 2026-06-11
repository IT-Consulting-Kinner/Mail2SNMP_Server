using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Infrastructure.Services;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Mail2SNMP.Tests.Infrastructure;

/// <summary>
/// Peer-review: the cluster-safety paths of WorkerLeaseService (license-edition
/// consensus and expired-lease reaping) were untested. These are the parts that
/// prevent a split-brain cluster from exceeding its licensed worker count or
/// from deadlocking on a crashed node's stale lease.
/// </summary>
public class WorkerLeaseClusterTests : IDisposable
{
    private readonly Mail2SnmpDbContext _db;
    private readonly ILicenseProvider _license = Substitute.For<ILicenseProvider>();

    public WorkerLeaseClusterTests()
    {
        var options = new DbContextOptionsBuilder<Mail2SnmpDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new Mail2SnmpDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SecondNode_WithDifferentLicenseEdition_RefusedToJoin()
    {
        // Generous instance limit so the *only* reason to refuse is edition mismatch.
        _license.GetLimit("maxworkerinstances").Returns(5);
        var svc = new WorkerLeaseService(_db, _license, NullLogger<WorkerLeaseService>.Instance);

        // Node 1 joins as Community and sets the cluster edition.
        _license.Current.Returns(new LicenseInfo { Edition = LicenseEdition.Community });
        Assert.True(await svc.TryAcquireLeaseAsync("worker-1"));

        // Node 2 comes up configured as Enterprise → must refuse to join.
        _license.Current.Returns(new LicenseInfo { Edition = LicenseEdition.Enterprise });
        Assert.False(await svc.TryAcquireLeaseAsync("worker-2"));

        var active = await svc.GetActiveLeasesAsync();
        Assert.Single(active);
    }

    [Fact]
    public async Task ExpiredLease_FromCrashedNode_IsReaped_AllowingNewAcquire()
    {
        _license.GetLimit("maxworkerinstances").Returns(1);
        _license.Current.Returns(new LicenseInfo { Edition = LicenseEdition.Community });

        // A crashed node left a stale lease whose heartbeat is well past the 90s timeout.
        _db.WorkerLeases.Add(new WorkerLease
        {
            InstanceId = "dead-worker",
            LicenseEdition = LicenseEdition.Community.ToString(),
            LastHeartbeatUtc = DateTime.UtcNow.AddMinutes(-10)
        });
        await _db.SaveChangesAsync();

        var svc = new WorkerLeaseService(_db, _license, NullLogger<WorkerLeaseService>.Instance);

        // With a 1-instance limit, acquisition only succeeds if the stale lease is reaped first.
        Assert.True(await svc.TryAcquireLeaseAsync("worker-new"));

        var active = await svc.GetActiveLeasesAsync();
        Assert.Single(active);
        Assert.Equal("worker-new", active[0].InstanceId);
    }
}
