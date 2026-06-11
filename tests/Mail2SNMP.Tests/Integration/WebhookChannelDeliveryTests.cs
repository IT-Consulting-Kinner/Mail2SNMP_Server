using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Core.Services;
using Mail2SNMP.Infrastructure.Channels;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Mail2SNMP.Tests.Integration;

/// <summary>
/// Peer-review: the existing WebhookDeliveryTests exercise a bare HttpClient
/// against WireMock and never touch the production code path. These tests drive
/// the REAL <see cref="WebhookNotificationChannel"/> end-to-end — template
/// rendering, the HTTP POST, success handling, and dead-lettering on failure.
/// </summary>
public class WebhookChannelDeliveryTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly Mail2SnmpDbContext _db;
    private readonly IDeadLetterService _deadLetter = Substitute.For<IDeadLetterService>();

    public WebhookChannelDeliveryTests()
    {
        _server = WireMockServer.Start();
        var options = new DbContextOptionsBuilder<Mail2SnmpDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new Mail2SnmpDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _server.Stop();
        _server.Dispose();
    }

    private WebhookNotificationChannel BuildChannel()
    {
        var license = Substitute.For<ILicenseProvider>();
        license.IsEnterprise().Returns(false); // Community → no HMAC signing

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());

        // WireMock binds to 127.0.0.1, so the SSRF guard would block it by default —
        // opt in to private targets for the test.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:AllowPrivateWebhookTargets"] = "true"
            })
            .Build();

        return new WebhookNotificationChannel(
            _db,
            Substitute.For<ICredentialEncryptor>(),
            license,
            _deadLetter,
            new TemplateEngine(NullLogger<TemplateEngine>.Instance),
            new FloodProtectionService(NullLogger<FloodProtectionService>.Instance),
            new NotificationDedupCache(),
            factory,
            config,
            NullLogger<WebhookNotificationChannel>.Instance);
    }

    private static NotificationContext Context(long eventId) => new()
    {
        EventId = eventId,
        JobName = "JobA",
        Subject = "Disk full on srv01",
        Severity = Severity.Critical,
        TimestampUtc = DateTime.UtcNow
    };

    [Fact]
    public async Task SendToWebhookTarget_Success_PostsAndDoesNotDeadLetter()
    {
        _server.Given(Request.Create().WithPath("/hook").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        var target = new WebhookTarget
        {
            Id = 1, Name = "T", Url = $"{_server.Url}/hook", IsActive = true, MaxRequestsPerMinute = 100
        };

        await BuildChannel().SendToWebhookTargetAsync(Context(eventId: 1), target);

        Assert.Single(_server.LogEntries);
        await _deadLetter.DidNotReceive().CreateAsync(Arg.Any<DeadLetterEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendToWebhookTarget_ServerError_CreatesDeadLetter()
    {
        _server.Given(Request.Create().WithPath("/hook-fail").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500));

        var target = new WebhookTarget
        {
            Id = 2, Name = "T", Url = $"{_server.Url}/hook-fail", IsActive = true, MaxRequestsPerMinute = 100
        };

        await BuildChannel().SendToWebhookTargetAsync(Context(eventId: 2), target);

        // A 5xx (EnsureSuccessStatusCode throws) must be caught and dead-lettered.
        await _deadLetter.Received(1).CreateAsync(
            Arg.Is<DeadLetterEntry>(d => d.WebhookTargetId == 2 && d.EventId == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendToWebhookTarget_PrivateTargetBlockedByDefault_DeadLetters()
    {
        // Without the opt-in, the SSRF guard rejects the loopback URL and the
        // delivery is dead-lettered rather than sent.
        var config = new ConfigurationBuilder().Build(); // AllowPrivateWebhookTargets = false
        var license = Substitute.For<ILicenseProvider>();
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());

        var channel = new WebhookNotificationChannel(
            _db, Substitute.For<ICredentialEncryptor>(), license, _deadLetter,
            new TemplateEngine(NullLogger<TemplateEngine>.Instance),
            new FloodProtectionService(NullLogger<FloodProtectionService>.Instance),
            new NotificationDedupCache(), factory, config,
            NullLogger<WebhookNotificationChannel>.Instance);

        _server.Given(Request.Create().WithPath("/blocked").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        var target = new WebhookTarget
        {
            Id = 3, Name = "T", Url = $"{_server.Url}/blocked", IsActive = true, MaxRequestsPerMinute = 100
        };

        await channel.SendToWebhookTargetAsync(Context(eventId: 3), target);

        Assert.Empty(_server.LogEntries); // never actually sent
        await _deadLetter.Received(1).CreateAsync(Arg.Any<DeadLetterEntry>(), Arg.Any<CancellationToken>());
    }
}
