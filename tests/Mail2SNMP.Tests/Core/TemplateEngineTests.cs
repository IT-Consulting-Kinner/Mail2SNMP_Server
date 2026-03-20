using Mail2SNMP.Core.Services;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mail2SNMP.Tests.Core;

public class TemplateEngineTests
{
    private readonly TemplateEngine _engine = new(NullLogger<TemplateEngine>.Instance);

    private NotificationContext CreateContext() => new()
    {
        EventId = 42,
        JobName = "TestJob",
        Mailbox = "inbox01",
        From = "alert@server.com",
        Subject = "Disk space low on /dev/sda1",
        Severity = Severity.Critical,
        RuleName = "DiskSpaceRule",
        HitCount = 3,
        TimestampUtc = new DateTime(2025, 3, 1, 12, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void BuildPayload_WithoutTemplate_ReturnsAllFields()
    {
        var payload = _engine.BuildPayload(CreateContext());
        Assert.Equal("TestJob", payload["JobName"].ToString());
        Assert.Equal("inbox01", payload["Mailbox"].ToString());
        Assert.Equal("alert@server.com", payload["From"].ToString());
        Assert.Equal("42", payload["EventId"].ToString());
        Assert.Equal("3", payload["HitCount"].ToString());
    }

    [Fact]
    public void RenderTemplate_ReplacesPlaceholders()
    {
        var template = "Alert: {Subject} from {From} (Job: {JobName})";
        var result = _engine.RenderTemplate(CreateContext(), template);
        Assert.Equal("Alert: Disk space low on /dev/sda1 from alert@server.com (Job: TestJob)", result);
    }

    [Fact]
    public void RenderTemplate_UnknownPlaceholder_LeavesAsLiteral()
    {
        var template = "Value: {UnknownField}";
        var result = _engine.RenderTemplate(CreateContext(), template);
        Assert.Equal("Value: {UnknownField}", result);
    }

    [Fact]
    public void TruncateForSnmp_ShortString_Unchanged()
    {
        var input = "Short string";
        Assert.Equal(input, _engine.TruncateForSnmp(input));
    }

    [Fact]
    public void TruncateForSnmp_LongString_Truncated()
    {
        var input = new string('A', 300);
        var result = _engine.TruncateForSnmp(input);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(result) <= 255);
    }

    [Fact]
    public void BuildPayload_TruncatesLongSubject()
    {
        var ctx = CreateContext();
        ctx.Subject = new string('X', 600);
        var payload = _engine.BuildPayload(ctx);
        var subject = payload["Subject"].ToString()!;
        Assert.True(subject.Length <= 500);
        Assert.EndsWith("...", subject);
    }

    [Fact]
    public void BuildPayload_TruncatesLongFrom()
    {
        var ctx = CreateContext();
        ctx.From = new string('Y', 600);
        var payload = _engine.BuildPayload(ctx);
        var from = payload["From"].ToString()!;
        Assert.True(from.Length <= 500);
        Assert.EndsWith("...", from);
    }

    [Fact]
    public void RenderTemplate_AllPlaceholders()
    {
        var template = "{JobName}|{Mailbox}|{From}|{Subject}|{Severity}|{EventId}|{HitCount}|{RuleName}";
        var result = _engine.RenderTemplate(CreateContext(), template);
        Assert.Contains("TestJob", result);
        Assert.Contains("Critical", result);
        Assert.Contains("DiskSpaceRule", result);
    }
}
