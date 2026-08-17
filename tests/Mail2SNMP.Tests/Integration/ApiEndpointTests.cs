using System.Net;
using System.Net.Http.Json;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Tests.Integration;

/// <summary>
/// Integration tests for the REST API endpoints using WebApplicationFactory.
/// Tests the full HTTP pipeline: routing, validation, serialization, and database round-trips.
/// Authentication is bypassed (AllowAnonymous fallback in Development).
/// </summary>
public class ApiEndpointTests : IClassFixture<TestWebApplicationFactory>, IDisposable
{
    private readonly HttpClient _client;

    public ApiEndpointTests(TestWebApplicationFactory factory)
    {
        // The factory itself is only needed to mint the client; we don't hold a
        // reference to it (it's owned by the xUnit IClassFixture lifetime).
        _client = factory.CreateClient();
    }

    public void Dispose() => _client.Dispose();

    // ── Mailbox Endpoints ────────────────────────────────────────────────

    [Fact]
    public async Task Mailboxes_CRUD_Lifecycle()
    {
        // Create
        var mailbox = new { Name = "IntTest-MB", Host = "imap.test.com", Port = 993, UseSsl = true, Username = "user@test.com", EncryptedPassword = "enc", Folder = "INBOX" };
        var createResponse = await _client.PostAsJsonAsync("/api/v1/mailboxes", mailbox);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<MailboxResponse>();
        Assert.NotNull(created);
        Assert.True(created!.Id > 0);
        Assert.Equal("IntTest-MB", created.Name);

        // Get all
        var allResponse = await _client.GetAsync("/api/v1/mailboxes");
        Assert.Equal(HttpStatusCode.OK, allResponse.StatusCode);

        // Update
        var update = new { created.Id, Name = "IntTest-MB-Updated", Host = "imap2.test.com", Port = 993, UseSsl = true, Username = "user@test.com", EncryptedPassword = "enc", Folder = "INBOX" };
        var updateResponse = await _client.PutAsJsonAsync($"/api/v1/mailboxes/{created.Id}", update);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        // Delete
        var deleteResponse = await _client.DeleteAsync($"/api/v1/mailboxes/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task SnmpTarget_Update_WithoutCredentials_PreservesThem()
    {
        // H-5 regression: GET never returns the encrypted credentials (only Has* flags),
        // so a normal read-modify-write client cannot echo them back. Because the update
        // is a full-row write, an omitted credential used to be persisted as blank —
        // silently downgrading an SNMPv3 target to unauthenticated cleartext.
        var create = new
        {
            Name = "H5-Snmp", Host = "10.0.0.9", Port = 162, Version = SnmpVersion.V3,
            SecurityName = "usm-user", EncryptedAuthPassword = "auth-secret",
            EncryptedPrivPassword = "priv-secret", MaxTrapsPerMinute = 100, IsActive = true
        };
        var created = await (await _client.PostAsJsonAsync("/api/v1/snmp-targets", create))
            .Content.ReadFromJsonAsync<SnmpTargetResponse>();
        Assert.NotNull(created);
        Assert.True(created!.HasAuthPassword);
        Assert.True(created.HasPrivPassword);

        // Read-modify-write exactly as a client would: change the name, send back what
        // GET gave us — which contains no credential fields at all.
        var update = new
        {
            created.Id, Name = "H5-Snmp-Renamed", Host = "10.0.0.9", Port = 162,
            Version = SnmpVersion.V3, SecurityName = "usm-user",
            MaxTrapsPerMinute = 100, IsActive = true
        };
        var updateResponse = await _client.PutAsJsonAsync($"/api/v1/snmp-targets/{created.Id}", update);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updated = await updateResponse.Content.ReadFromJsonAsync<SnmpTargetResponse>();
        Assert.NotNull(updated);
        Assert.Equal("H5-Snmp-Renamed", updated!.Name);
        Assert.True(updated.HasAuthPassword, "auth password must survive an update that omits it");
        Assert.True(updated.HasPrivPassword, "priv password must survive an update that omits it");

        await _client.DeleteAsync($"/api/v1/snmp-targets/{created.Id}");
    }

    [Fact]
    public async Task WebhookTarget_Update_WithoutSecret_PreservesIt()
    {
        // H-5 regression for the webhook HMAC signing secret: losing it silently turns
        // signed deliveries into unsigned ones.
        var create = new
        {
            Name = "H5-Hook", Url = "https://example.com/hook",
            EncryptedSecret = "signing-secret", MaxRequestsPerMinute = 60, IsActive = true
        };
        var created = await (await _client.PostAsJsonAsync("/api/v1/webhook-targets", create))
            .Content.ReadFromJsonAsync<WebhookTargetResponse>();
        Assert.NotNull(created);
        Assert.True(created!.HasSecret);

        var update = new { created.Id, Name = "H5-Hook-Renamed", Url = "https://example.com/hook", MaxRequestsPerMinute = 60, IsActive = true };
        var updateResponse = await _client.PutAsJsonAsync($"/api/v1/webhook-targets/{created.Id}", update);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updated = await updateResponse.Content.ReadFromJsonAsync<WebhookTargetResponse>();
        Assert.NotNull(updated);
        Assert.Equal("H5-Hook-Renamed", updated!.Name);
        Assert.True(updated.HasSecret, "signing secret must survive an update that omits it");

        await _client.DeleteAsync($"/api/v1/webhook-targets/{created.Id}");
    }

    [Fact]
    public async Task Mailboxes_Create_InvalidModel_ReturnsBadRequest()
    {
        var invalid = new { Name = "", Host = "", Username = "", EncryptedPassword = "", Folder = "" };
        var response = await _client.PostAsJsonAsync("/api/v1/mailboxes", invalid);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Rule Endpoints ───────────────────────────────────────────────────

    [Fact]
    public async Task Rules_CRUD_Lifecycle()
    {
        var rule = new { Name = "IntTest-Rule", Field = RuleFieldType.Subject, MatchType = RuleMatchType.Contains, Criteria = "ALERT", Severity = Severity.Warning };
        var createResponse = await _client.PostAsJsonAsync("/api/v1/rules", rule);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<Rule>();
        Assert.NotNull(created);
        Assert.Equal("IntTest-Rule", created!.Name);

        // Get by ID
        var getResponse = await _client.GetAsync($"/api/v1/rules/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        // Delete
        var deleteResponse = await _client.DeleteAsync($"/api/v1/rules/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Get after delete → 404
        var notFoundResponse = await _client.GetAsync($"/api/v1/rules/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, notFoundResponse.StatusCode);
    }

    // ── Job Endpoints ────────────────────────────────────────────────────

    [Fact]
    public async Task Jobs_Create_RequiresMailboxAndRule()
    {
        // Create prerequisite mailbox
        var mb = new { Name = "Job-MB", Host = "imap.test.com", Port = 993, UseSsl = true, Username = "u", EncryptedPassword = "e", Folder = "INBOX" };
        var mbResponse = await _client.PostAsJsonAsync("/api/v1/mailboxes", mb);
        Assert.Equal(HttpStatusCode.Created, mbResponse.StatusCode);
        var mailbox = await mbResponse.Content.ReadFromJsonAsync<MailboxResponse>();

        // Create prerequisite rule
        var rl = new { Name = "Job-Rule", Field = RuleFieldType.Subject, MatchType = RuleMatchType.Contains, Criteria = "test" };
        var rlResponse = await _client.PostAsJsonAsync("/api/v1/rules", rl);
        Assert.Equal(HttpStatusCode.Created, rlResponse.StatusCode);
        var rule = await rlResponse.Content.ReadFromJsonAsync<Rule>();

        // Create job referencing the mailbox and rule (using JobRequest DTO)
        var job = new { Name = "IntTest-Job", MailboxId = mailbox!.Id, RuleId = rule!.Id, SnmpTargetIds = Array.Empty<int>(), WebhookTargetIds = Array.Empty<int>() };
        var jobResponse = await _client.PostAsJsonAsync("/api/v1/jobs", job);
        Assert.Equal(HttpStatusCode.Created, jobResponse.StatusCode);
    }

    // ── Schedule Endpoints ───────────────────────────────────────────────

    [Fact]
    public async Task Schedules_GetAll_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/v1/schedules");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Dashboard Endpoint ───────────────────────────────────────────────

    [Fact]
    public async Task Dashboard_Get_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/v1/dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── License Endpoint ─────────────────────────────────────────────────

    [Fact]
    public async Task License_Get_ReturnsCommunity()
    {
        var response = await _client.GetAsync("/api/v1/license");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        // LicenseEdition.Community serializes as numeric 0 by default
        Assert.Contains("\"edition\":0", content.Replace(" ", ""));
    }

    // ── Dead Letter Endpoints ────────────────────────────────────────────

    [Fact]
    public async Task DeadLetters_GetAll_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/v1/dead-letters");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeadLetters_Get_StaysABareArrayAndReportsTheTotalInAHeader()
    {
        // Adding filters and paging must not reshape the body: 1.1.0 clients and scripts
        // parse a bare JSON array. The pre-paging total goes in X-Total-Count instead of
        // an envelope.
        var response = await _client.GetAsync("/api/v1/dead-letters?status=Abandoned&kind=Snmp&skip=0&take=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = (await response.Content.ReadAsStringAsync()).TrimStart();
        Assert.StartsWith("[", body);

        Assert.True(response.Headers.TryGetValues("X-Total-Count", out var totals));
        Assert.True(int.TryParse(totals.Single(), out _));
    }

    [Fact]
    public async Task DeadLetters_Get_RejectsAnUnparseableFilterValue()
    {
        // Minimal-API binding turns a bad enum value into a 400 rather than silently
        // ignoring the filter and returning the unfiltered queue.
        var response = await _client.GetAsync("/api/v1/dead-letters?status=NotAStatus");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeadLetters_RetryAll_WithNoTarget_IsAcceptedAndReportsACount()
    {
        // The pre-1.2 route took a webhookTargetId, so SNMP entries had no bulk path at
        // all. The kind-neutral route must accept an empty filter.
        var response = await _client.PostAsync("/api/v1/dead-letters/retry-all", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("count", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeadLetters_RetryAll_LegacyPerWebhookTargetRouteStillWorks()
    {
        var response = await _client.PostAsync("/api/v1/dead-letters/retry-all/1", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── API parity for the UI-only 1.1.0 features ────────────────────────

    [Fact]
    public async Task MailLog_IsReachableFromTheApi_WithFiltersAndATotal()
    {
        // UC-5 shipped as a UI-only page, so "why did this mail produce no trap?" could
        // only be asked by a human with a browser.
        var response = await _client.GetAsync("/api/v1/mail-log?disposition=NoMatch&take=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = (await response.Content.ReadAsStringAsync()).TrimStart();
        Assert.StartsWith("[", body);
        Assert.True(response.Headers.TryGetValues("X-Total-Count", out var totals));
        Assert.True(int.TryParse(totals.Single(), out _));
    }

    [Fact]
    public async Task MailLog_RejectsAnUnparseableDisposition()
    {
        var response = await _client.GetAsync("/api/v1/mail-log?disposition=NotADisposition");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TestSend_IsReachableFromTheApi_AndHonoursTheSeverity()
    {
        var job = await CreateMinimalJobAsync("Parity-TestSend");

        // UC-7 parity: the operation that proves a job's whole delivery path works was
        // previously a button and nothing else.
        var response = await _client.PostAsync($"/api/v1/jobs/{job.Id}/test-send?severity=Critical", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Critical", content);
        Assert.Contains("report", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestSend_OnAMissingJob_Is404()
    {
        var response = await _client.PostAsync("/api/v1/jobs/999999/test-send", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BulkJobs_DeactivatesEveryListedJob()
    {
        var a = await CreateMinimalJobAsync("Parity-BulkA");
        var b = await CreateMinimalJobAsync("Parity-BulkB");

        // UX-5 parity: emulating this with a loop of read-modify-write PUTs is how a
        // concurrent edit gets clobbered.
        var response = await _client.PostAsJsonAsync("/api/v1/jobs/bulk",
            new { Ids = new[] { a.Id, b.Id }, Action = "Deactivate" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        foreach (var id in new[] { a.Id, b.Id })
        {
            var reloaded = await _client.GetFromJsonAsync<JobResponse>($"/api/v1/jobs/{id}");
            Assert.False(reloaded!.IsActive);
        }
    }

    [Fact]
    public async Task BulkJobs_ReportsPerIdFailuresInsteadOfFailingTheBatch()
    {
        var real = await CreateMinimalJobAsync("Parity-BulkPartial");

        var response = await _client.PostAsJsonAsync("/api/v1/jobs/bulk",
            new { Ids = new[] { real.Id, 999999 }, Action = "Deactivate" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        // The caller asked for several independent operations, not a transaction.
        Assert.Contains("999999", content);
        Assert.Contains("succeeded", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BulkJobs_RejectsAnEmptyIdList()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/jobs/bulk",
            new { Ids = Array.Empty<int>(), Action = "Deactivate" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Creates a mailbox, rule and job so parity tests have something to act on.</summary>
    private async Task<JobResponse> CreateMinimalJobAsync(string name)
    {
        var mailbox = await (await _client.PostAsJsonAsync("/api/v1/mailboxes", new
        {
            Name = $"{name}-MB", Host = "imap.test.invalid", Port = 993, UseSsl = true,
            Username = "u", EncryptedPassword = "enc", Folder = "INBOX"
        })).Content.ReadFromJsonAsync<MailboxResponse>();

        // The rules endpoint returns the entity itself rather than a response DTO.
        var rule = await (await _client.PostAsJsonAsync("/api/v1/rules", new
        {
            Name = $"{name}-Rule", Field = RuleFieldType.Subject,
            MatchType = RuleMatchType.Contains, Criteria = "ALERT", Severity = Severity.Error
        })).Content.ReadFromJsonAsync<Rule>();

        var created = await _client.PostAsJsonAsync("/api/v1/jobs", new
        {
            Name = name, MailboxId = mailbox!.Id, RuleId = rule!.Id,
            MaxEventsPerHour = 100, MaxActiveEvents = 100, IsActive = true,
            SnmpTargetIds = Array.Empty<int>(), WebhookTargetIds = Array.Empty<int>()
        });
        return (await created.Content.ReadFromJsonAsync<JobResponse>())!;
    }

    // ── Worker Endpoints ─────────────────────────────────────────────────

    [Fact]
    public async Task Workers_GetAll_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/v1/workers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Health Endpoints ─────────────────────────────────────────────────

    [Fact]
    public async Task HealthReady_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthLive_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Not Found ────────────────────────────────────────────────────────

    [Fact]
    public async Task NonExistent_Mailbox_GetById_ReturnsMethodNotAllowed()
    {
        // Mailbox endpoints have PUT/DELETE /{id} but no GET /{id} — expect 405
        var response = await _client.GetAsync("/api/v1/mailboxes/99999");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task NonExistent_Rule_Returns404()
    {
        var response = await _client.GetAsync("/api/v1/rules/99999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>DTO for deserialization (mailbox endpoint returns a response without the encrypted password).</summary>
    private record MailboxResponse(int Id, string Name, string Host, int Port, bool UseSsl, string Username, string Folder, bool IsActive);
}
