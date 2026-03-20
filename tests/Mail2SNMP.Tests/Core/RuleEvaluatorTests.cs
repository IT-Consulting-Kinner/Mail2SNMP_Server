using Mail2SNMP.Core.Services;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mail2SNMP.Tests.Core;

public class RuleEvaluatorTests
{
    private readonly RuleEvaluator _evaluator = new(NullLogger<RuleEvaluator>.Instance);

    [Fact]
    public void Evaluate_SubjectContains_Matches()
    {
        var rule = new Rule { Field = RuleFieldType.Subject, MatchType = RuleMatchType.Contains, Criteria = "ALERT" };
        Assert.True(_evaluator.Evaluate(rule, "test@test.com", "Server ALERT: CPU high", null));
    }

    [Fact]
    public void Evaluate_SubjectContains_NoMatch()
    {
        var rule = new Rule { Field = RuleFieldType.Subject, MatchType = RuleMatchType.Contains, Criteria = "ALERT" };
        Assert.False(_evaluator.Evaluate(rule, "test@test.com", "Normal operation report", null));
    }

    [Fact]
    public void Evaluate_FromEquals_CaseInsensitive()
    {
        var rule = new Rule { Field = RuleFieldType.From, MatchType = RuleMatchType.Equals, Criteria = "noreply@monitoring.com" };
        Assert.True(_evaluator.Evaluate(rule, "NOREPLY@monitoring.COM", "Test", null));
    }

    [Fact]
    public void Evaluate_SubjectStartsWith_Matches()
    {
        var rule = new Rule { Field = RuleFieldType.Subject, MatchType = RuleMatchType.StartsWith, Criteria = "[CRITICAL]" };
        Assert.True(_evaluator.Evaluate(rule, "test@test.com", "[CRITICAL] Disk space low", null));
    }

    [Fact]
    public void Evaluate_SubjectEndsWith_Matches()
    {
        var rule = new Rule { Field = RuleFieldType.Subject, MatchType = RuleMatchType.EndsWith, Criteria = "FAILED" };
        Assert.True(_evaluator.Evaluate(rule, "test@test.com", "Backup job FAILED", null));
    }

    [Fact]
    public void Evaluate_SubjectRegex_Matches()
    {
        var rule = new Rule { Field = RuleFieldType.Subject, MatchType = RuleMatchType.Regex, Criteria = @"CPU\s+\d+%" };
        Assert.True(_evaluator.Evaluate(rule, "test@test.com", "Server CPU 95% warning", null));
    }

    [Fact]
    public void Evaluate_SubjectRegex_InvalidPattern_ReturnsFalse()
    {
        var rule = new Rule { Field = RuleFieldType.Subject, MatchType = RuleMatchType.Regex, Criteria = "[invalid" };
        Assert.False(_evaluator.Evaluate(rule, "test@test.com", "Test subject", null));
    }

    [Fact]
    public void Evaluate_BodyContains_Matches()
    {
        var rule = new Rule { Field = RuleFieldType.Body, MatchType = RuleMatchType.Contains, Criteria = "error code 500" };
        Assert.True(_evaluator.Evaluate(rule, "test@test.com", "Subject", "Application returned error code 500 at 10:00"));
    }

    [Fact]
    public void Evaluate_NullSubject_NoException()
    {
        var rule = new Rule { Field = RuleFieldType.Subject, MatchType = RuleMatchType.Contains, Criteria = "test" };
        Assert.False(_evaluator.Evaluate(rule, "test@test.com", null, null));
    }

    [Fact]
    public void Evaluate_NullFrom_NoException()
    {
        var rule = new Rule { Field = RuleFieldType.From, MatchType = RuleMatchType.Equals, Criteria = "test@test.com" };
        Assert.False(_evaluator.Evaluate(rule, null, "Subject", null));
    }
}
