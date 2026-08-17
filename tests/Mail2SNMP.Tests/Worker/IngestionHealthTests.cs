using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Infrastructure.Services;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Mail2SNMP.Tests.Worker;

/// <summary>
/// Review finding: a broken ingestion path was visible only by pulling — the dashboard's
/// red banner, if somebody opened it. Nothing was pushed.
/// </summary>
/// <remarks>
/// This is the one failure the product structurally could not report. Every other alert it
/// sends is derived from an inbound mail; a mailbox that stops polling produces no mail,
/// therefore no event, therefore no notification — so a dead ingestion path is
/// indistinguishable from a quiet night. These tests pin the two properties that make the
/// new signal useful rather than annoying: it fires on <em>transition</em> (one alarm per
/// outage, not one per minute), and it fires again on recovery so the alarm can be cleared.
/// </remarks>
public class IngestionHealthTests : IDisposable
{
    private readonly Mail2SnmpDbContext _db;
    private readonly ServiceProvider _provider;
    private readonly RecordingChannel _channel = new();
    private readonly IWorkerLeaseService _lease = Substitute.For<IWorkerLeaseService>();

    private static string SelfInstanceId => $"{Environment.MachineName}-{Environment.ProcessId}";

    /// <summary>Records every ingestion-health notification it is asked to send.</summary>
    private sealed class RecordingChannel : INotificationChannel
    {
        public string ChannelName => INotificationChannel.Snmp;
        public List<(bool Degraded, string Message)> Sent { get; } = new();

        public Task SendIngestionHealthAsync(bool degraded, string message, CancellationToken ct = default)
        {
            Sent.Add((degraded, message));
            return Task.CompletedTask;
        }
    }

    public IngestionHealthTests()
    {
        var options = new DbContextOptionsBuilder<Mail2SnmpDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new Mail2SnmpDbContext(options);
        _db.Database.EnsureCreated();

        // This instance must appear in the active lease set to be eligible as primary —
        // an instance whose heartbeat has not landed yet deliberately defers rather than
        // assuming leadership. The service derives its id the same way HeartbeatService
        // records it.
        _lease.GetActiveLeasesAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new WorkerLease { InstanceId = SelfInstanceId } });

        var services = new ServiceCollection();
        services.AddSingleton(_db);
        services.AddSingleton(_lease);
        services.AddSingleton<IEnumerable<INotificationChannel>>(new[] { (INotificationChannel)_channel });
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _db.Dispose();
    }

    private IngestionHealthService NewService()
        => new(_provider.GetRequiredService<IServiceScopeFactory>(),
               NullLogger<IngestionHealthService>.Instance);

    private async Task<Mailbox> AddMailboxAsync(string name, string? error = null, bool active = true)
    {
        var mailbox = new Mailbox
        {
            Name = name, Host = "h", Username = "u", EncryptedPassword = "p",
            Folder = "INBOX", IsActive = active, LastError = error
        };
        _db.Mailboxes.Add(mailbox);
        await _db.SaveChangesAsync();
        return mailbox;
    }

    [Fact]
    public async Task ANonPrimaryInstance_StaysSilent()
    {
        // Otherwise every node in a cluster reports the same outage and the NOC gets N
        // identical alarms for one problem.
        await AddMailboxAsync("Alerts", error: "boom");
        _lease.GetActiveLeasesAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new WorkerLease { InstanceId = "AAA-first" },
                new WorkerLease { InstanceId = SelfInstanceId }
            });

        await NewService().CheckAsync(CancellationToken.None);

        Assert.Empty(_channel.Sent);
        // The gauge is still updated: it describes this instance's view of current state
        // and must not go stale just because another node owns the notification.
        Assert.Equal(1, Mail2SnmpMetrics.MailboxesInError.Value);
    }

    [Fact]
    public async Task AFailingMailbox_RaisesAnAlarmNamingIt()
    {
        await AddMailboxAsync("Alerts", error: "Authentication failed");
        var svc = NewService();

        await svc.CheckAsync(CancellationToken.None);

        var sent = Assert.Single(_channel.Sent);
        Assert.True(sent.Degraded);
        // Naming the mailbox is the difference between an actionable alarm and "something
        // is wrong somewhere".
        Assert.Contains("Alerts", sent.Message);
        Assert.Equal(1, Mail2SnmpMetrics.MailboxesInError.Value);
    }

    [Fact]
    public async Task TheAlarmIsRaisedOncePerOutage_NotOncePerCheck()
    {
        await AddMailboxAsync("Alerts", error: "Authentication failed");
        var svc = NewService();

        for (var i = 0; i < 5; i++)
            await svc.CheckAsync(CancellationToken.None);

        // A trap per minute for the same broken mailbox is noise an operator learns to
        // filter, which is worse than no trap at all.
        Assert.Single(_channel.Sent);
    }

    [Fact]
    public async Task RecoveryIsAnnounced_SoTheAlarmCanBeCleared()
    {
        var mailbox = await AddMailboxAsync("Alerts", error: "Authentication failed");
        var svc = NewService();
        await svc.CheckAsync(CancellationToken.None);

        mailbox.LastError = null;
        await _db.SaveChangesAsync();
        await svc.CheckAsync(CancellationToken.None);

        Assert.Equal(2, _channel.Sent.Count);
        Assert.True(_channel.Sent[0].Degraded);
        // Without this a NOC has no way to close the alarm except a human deciding it
        // looks fine now.
        Assert.False(_channel.Sent[1].Degraded);
        Assert.Equal(0, Mail2SnmpMetrics.MailboxesInError.Value);
    }

    [Fact]
    public async Task AHealthySystem_AnnouncesNothingOnStartup()
    {
        await AddMailboxAsync("Alerts");
        var svc = NewService();

        await svc.CheckAsync(CancellationToken.None);

        // Healthy is the normal state, not a recovery from anything — announcing it on
        // every restart would train operators to ignore the recovery notification.
        Assert.Empty(_channel.Sent);
        Assert.Equal(0, Mail2SnmpMetrics.MailboxesInError.Value);
    }

    [Fact]
    public async Task InactiveMailboxes_AreNotCountedAsFailures()
    {
        // A deactivated mailbox is not polled by design; reporting it as an outage would
        // make the alarm permanently true for anyone who retires a mailbox.
        await AddMailboxAsync("Retired", error: "Authentication failed", active: false);
        await AddMailboxAsync("Alerts");
        var svc = NewService();

        await svc.CheckAsync(CancellationToken.None);

        Assert.Empty(_channel.Sent);
        Assert.Equal(0, Mail2SnmpMetrics.MailboxesInError.Value);
    }

    [Fact]
    public async Task TheGaugeTracksTheCount_EvenWithoutATransition()
    {
        var first = await AddMailboxAsync("A", error: "boom");
        await AddMailboxAsync("B");
        var svc = NewService();
        await svc.CheckAsync(CancellationToken.None);
        Assert.Equal(1, Mail2SnmpMetrics.MailboxesInError.Value);

        var second = await _db.Mailboxes.FirstAsync(m => m.Name == "B");
        second.LastError = "boom too";
        await _db.SaveChangesAsync();
        await svc.CheckAsync(CancellationToken.None);

        // Still degraded, so no new notification — but the gauge describes current state
        // and would be misleading if it went stale.
        Assert.Single(_channel.Sent);
        Assert.Equal(2, Mail2SnmpMetrics.MailboxesInError.Value);
    }

    [Fact]
    public async Task ABrokenChannelDoesNotStopTheOthersFromReporting()
    {
        await AddMailboxAsync("Alerts", error: "boom");

        var throwing = Substitute.For<INotificationChannel>();
        throwing.ChannelName.Returns(INotificationChannel.Webhook);
        throwing.SendIngestionHealthAsync(Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new HttpRequestException("target unreachable"));

        var services = new ServiceCollection();
        services.AddSingleton(_db);
        services.AddSingleton(_lease);
        services.AddSingleton<IEnumerable<INotificationChannel>>(new[] { throwing, (INotificationChannel)_channel });
        using var provider = services.BuildServiceProvider();

        var svc = new IngestionHealthService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IngestionHealthService>.Instance);

        await svc.CheckAsync(CancellationToken.None);

        // The whole point is reporting an outage; one unreachable target must not
        // suppress the report to every other one.
        Assert.Single(_channel.Sent);
    }
}
