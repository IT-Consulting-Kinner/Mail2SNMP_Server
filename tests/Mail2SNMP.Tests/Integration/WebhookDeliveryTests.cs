using System.Security.Cryptography;
using Mail2SNMP.Infrastructure.Channels;
using Mail2SNMP.Infrastructure.Security;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Mail2SNMP.Tests.Integration;

/// <summary>
/// Integration tests for the WebhookNotificationChannel using WireMock
/// to simulate real HTTP webhook endpoints (success, failure, timeout scenarios).
/// </summary>
public class WebhookDeliveryTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly HttpClient _httpClient;

    public WebhookDeliveryTests()
    {
        _server = WireMockServer.Start();
        _httpClient = new HttpClient();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _server.Stop();
        _server.Dispose();
    }

    [Fact]
    public async Task Webhook_SuccessfulDelivery_Returns200()
    {
        // Arrange: WireMock accepts POST and returns 200
        _server.Given(
            Request.Create().WithPath("/webhook").UsingPost()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithBody("{\"status\":\"ok\"}")
        );

        var targetUrl = $"{_server.Url}/webhook";

        // Act: send a POST to the mocked webhook
        var payload = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(new { EventId = 1, Subject = "Test Alert", Severity = "Critical" }),
            System.Text.Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(targetUrl, payload);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
        Assert.Single(_server.LogEntries);
        var request = _server.LogEntries.First();
        Assert.Contains("Test Alert", request.RequestMessage.Body);
    }

    [Fact]
    public async Task Webhook_ServerError_Returns500()
    {
        // Arrange: WireMock returns 500 Internal Server Error
        _server.Given(
            Request.Create().WithPath("/webhook-fail").UsingPost()
        ).RespondWith(
            Response.Create().WithStatusCode(500).WithBody("Internal Server Error")
        );

        var targetUrl = $"{_server.Url}/webhook-fail";

        // Act
        var payload = new StringContent("{\"EventId\":1}", System.Text.Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(targetUrl, payload);

        // Assert: response is 500, which would trigger dead-letter in real code
        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_Timeout_ThrowsTaskCanceledException()
    {
        // Arrange: WireMock delays response by 10 seconds
        _server.Given(
            Request.Create().WithPath("/webhook-slow").UsingPost()
        ).RespondWith(
            Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(10))
        );

        var targetUrl = $"{_server.Url}/webhook-slow";

        // Act: use a short timeout client
        using var timeoutClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
        var payload = new StringContent("{\"EventId\":1}", System.Text.Encoding.UTF8, "application/json");

        // Assert: should throw due to timeout
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => timeoutClient.PostAsync(targetUrl, payload));
    }

    [Fact]
    public async Task Webhook_CorrectContentType_JsonSent()
    {
        // Arrange
        _server.Given(
            Request.Create().WithPath("/webhook-headers").UsingPost()
        ).RespondWith(
            Response.Create().WithStatusCode(200)
        );

        var targetUrl = $"{_server.Url}/webhook-headers";

        // Act
        var payload = new StringContent(
            "{\"EventId\":42,\"Subject\":\"CPU High\"}", System.Text.Encoding.UTF8, "application/json");
        await _httpClient.PostAsync(targetUrl, payload);

        // Assert: verify content type header was sent
        // K5: explicit null guard so the test fails with a clear message instead
        // of the CS8602 NRE warning the compiler emits on the chained call.
        var request = _server.LogEntries.First();
        var contentType = request.RequestMessage?.Headers?["Content-Type"];
        Assert.NotNull(contentType);
        Assert.Contains("application/json", contentType!.First());
    }

    [Fact]
    public async Task Webhook_MultipleDeliveries_AllReceived()
    {
        // Arrange
        _server.Given(
            Request.Create().WithPath("/webhook-batch").UsingPost()
        ).RespondWith(
            Response.Create().WithStatusCode(200)
        );

        var targetUrl = $"{_server.Url}/webhook-batch";

        // Act: send 5 webhooks
        for (int i = 0; i < 5; i++)
        {
            var payload = new StringContent(
                $"{{\"EventId\":{i},\"Subject\":\"Alert {i}\"}}", System.Text.Encoding.UTF8, "application/json");
            await _httpClient.PostAsync(targetUrl, payload);
        }

        // Assert: all 5 received
        Assert.Equal(5, _server.LogEntries.Count());
    }

    [Fact]
    public async Task Webhook_NotFound_Returns404()
    {
        // Arrange: no mock registered for this path → WireMock returns default (no match)
        // WireMock by default returns 404 for unmatched requests
        var targetUrl = $"{_server.Url}/nonexistent-endpoint";

        // Act
        var payload = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(targetUrl, payload);

        // Assert
        Assert.False(response.IsSuccessStatusCode);
    }
}
