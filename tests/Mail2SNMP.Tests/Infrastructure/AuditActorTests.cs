using System.Security.Claims;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Core.Services;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Infrastructure.Security;
using Mail2SNMP.Infrastructure.Services;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Mail2SNMP.Tests.Infrastructure;

/// <summary>
/// Review finding: every configuration mutation was audited as
/// <see cref="ActorType.System"/> / <c>"system"</c>, so the audit log recorded that a job
/// was deleted but never by whom — the one question an audit trail exists to answer. Login
/// and event-lifecycle actions already carried a real actor; configuration changes did not.
/// </summary>
public class AuditActorTests : IDisposable
{
    private readonly Mail2SnmpDbContext _db;

    public AuditActorTests()
    {
        var options = new DbContextOptionsBuilder<Mail2SnmpDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new Mail2SnmpDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() => _db.Dispose();

    private AuditService NewAudit(ICurrentActor actor)
        => new(_db, actor, NullLogger<AuditService>.Instance);

    /// <summary>Builds an accessor whose HttpContext carries the given principal, or none at all.</summary>
    private static IHttpContextAccessor Accessor(ClaimsPrincipal? principal)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        if (principal is null)
        {
            accessor.HttpContext.Returns((HttpContext?)null);
        }
        else
        {
            var ctx = new DefaultHttpContext { User = principal };
            accessor.HttpContext.Returns(ctx);
        }
        return accessor;
    }

    private static ClaimsPrincipal SignedInUser(string name)
        => new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, name) }, "TestCookie"));

    private static ClaimsPrincipal ApiKeyPrincipal(string keyName, int keyId)
        => new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, keyName),
            new Claim("ApiKeyId", keyId.ToString())
        }, "ApiKey"));

    // ---------------------------------------------------------- actor resolution

    [Fact]
    public void SignedInUser_IsReportedAsThatUser()
    {
        var actor = new HttpContextCurrentActor(Accessor(SignedInUser("kevin@example.invalid")));

        Assert.Equal(ActorType.User, actor.Type);
        Assert.Equal("kevin@example.invalid", actor.Id);
    }

    [Fact]
    public void ApiKey_IsDistinguishableFromAHumanAndNeverExposesTheKey()
    {
        var actor = new HttpContextCurrentActor(Accessor(ApiKeyPrincipal("ci-pipeline", 7)));

        // Service, not User: an automated change should not read like someone sat down
        // and made it.
        Assert.Equal(ActorType.Service, actor.Type);
        Assert.Equal("apikey:ci-pipeline", actor.Id);
    }

    [Fact]
    public void NoRequest_FallsBackToSystem()
    {
        // A hosted service running inside the web host has no ambient request; "system"
        // is the honest answer there, not a guess.
        var actor = new HttpContextCurrentActor(Accessor(null));

        Assert.Equal(ActorType.System, actor.Type);
        Assert.Equal("system", actor.Id);
    }

    [Fact]
    public void UnauthenticatedRequest_FallsBackToSystem()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        var actor = new HttpContextCurrentActor(Accessor(anonymous));

        Assert.Equal(ActorType.System, actor.Type);
    }

    [Fact]
    public void CliActor_IsDistinguishableFromAScheduledChange()
    {
        var actor = new SystemCurrentActor("cli:CORP\\kevin");

        Assert.Equal(ActorType.System, actor.Type);
        Assert.Equal("cli:CORP\\kevin", actor.Id);
    }

    // ---------------------------------------------------------- end-to-end attribution

    [Fact]
    public async Task ConfigurationChange_NamesTheUserWhoMadeIt()
    {
        var actor = new HttpContextCurrentActor(Accessor(SignedInUser("kevin@example.invalid")));
        var svc = new RuleService(_db, NewAudit(actor));

        var rule = await svc.CreateAsync(new Rule
        {
            Name = "R", Field = RuleFieldType.Subject,
            MatchType = RuleMatchType.Contains, Criteria = "x"
        });
        await svc.DeleteAsync(rule.Id);

        var entries = await _db.AuditEvents.AsNoTracking()
            .Where(a => a.TargetType == "Rule").ToListAsync();

        Assert.Equal(2, entries.Count);
        // Before the fix both of these read System/"system" and the log was useless for
        // answering who changed the configuration.
        Assert.All(entries, e => Assert.Equal(ActorType.User, e.ActorType));
        Assert.All(entries, e => Assert.Equal("kevin@example.invalid", e.ActorId));
        Assert.Contains(entries, e => e.Action == "Rule.Created");
        Assert.Contains(entries, e => e.Action == "Rule.Deleted");
    }

    [Fact]
    public async Task WorkerChange_IsStillRecordedAsSystem()
    {
        // The worker has no user, and pretending otherwise would be worse than "system".
        var svc = new RuleService(_db, NewAudit(new SystemCurrentActor()));

        await svc.CreateAsync(new Rule
        {
            Name = "R", Field = RuleFieldType.Subject,
            MatchType = RuleMatchType.Contains, Criteria = "x"
        });

        var entry = await _db.AuditEvents.AsNoTracking().SingleAsync(a => a.TargetType == "Rule");
        Assert.Equal(ActorType.System, entry.ActorType);
        Assert.Equal("system", entry.ActorId);
    }

    [Fact]
    public async Task ExplicitActorOverload_StillWins()
    {
        // Failed sign-ins must name the attempted account, which by definition is not the
        // ambient (unauthenticated) actor.
        var svc = NewAudit(new HttpContextCurrentActor(Accessor(null)));

        await svc.LogAsync(ActorType.System, "attacker@example.invalid", "User.LoginFailed",
            "User", "attacker@example.invalid", result: AuditResult.Failure);

        var entry = await _db.AuditEvents.AsNoTracking().SingleAsync();
        Assert.Equal("attacker@example.invalid", entry.ActorId);
        Assert.Equal(AuditResult.Failure, entry.Result);
    }
}
