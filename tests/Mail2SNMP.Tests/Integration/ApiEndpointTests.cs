using System.Net;
using System.Net.Http.Json;
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
    private readonly TestWebApplicationFactory _factory;

    public ApiEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
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
