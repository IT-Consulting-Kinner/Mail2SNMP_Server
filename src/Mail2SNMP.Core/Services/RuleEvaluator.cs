using System.Text.RegularExpressions;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Core.Services;

/// <summary>
/// Evaluates email parsing rules against incoming email fields
/// (Subject, From, To, Body, Header). Each rule specifies a field,
/// a match type, and criteria that are tested against the corresponding
/// email value.
/// </summary>
public class RuleEvaluator
{
    private readonly ILogger<RuleEvaluator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RuleEvaluator"/> class.
    /// </summary>
    /// <param name="logger">Logger used to record warnings for invalid or timed-out regex patterns.</param>
    public RuleEvaluator(ILogger<RuleEvaluator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Evaluates a single rule against the provided email fields.
    /// The rule's <see cref="Rule.Field"/> determines which email value is inspected,
    /// and the rule's <see cref="Rule.MatchType"/> determines the comparison strategy.
    /// </summary>
    /// <param name="rule">The rule to evaluate.</param>
    /// <param name="from">The sender address of the email.</param>
    /// <param name="subject">The subject line of the email.</param>
    /// <param name="body">The plain-text body of the email.</param>
    /// <param name="headers">An optional dictionary of email headers keyed by header name.</param>
    /// <returns><c>true</c> if the rule matches the email; otherwise <c>false</c>.</returns>
    public bool Evaluate(Rule rule, string? from, string? subject, string? body, IDictionary<string, string>? headers = null)
    {
        var fieldValue = rule.Field switch
        {
            RuleFieldType.Subject => subject ?? string.Empty,
            RuleFieldType.From => from ?? string.Empty,
            RuleFieldType.To => TryGetHeader(headers, "To"),
            RuleFieldType.Body => body ?? string.Empty,
            RuleFieldType.Header => TryGetHeader(headers, rule.Criteria.Split(':').FirstOrDefault() ?? ""),
            _ => string.Empty
        };

        var criteria = rule.Field == RuleFieldType.Header && rule.Criteria.Contains(':')
            ? rule.Criteria.Split(':', 2).Last()
            : rule.Criteria;

        return rule.MatchType switch
        {
            RuleMatchType.Contains => fieldValue.Contains(criteria, StringComparison.OrdinalIgnoreCase),
            RuleMatchType.Equals => fieldValue.Equals(criteria, StringComparison.OrdinalIgnoreCase),
            RuleMatchType.StartsWith => fieldValue.StartsWith(criteria, StringComparison.OrdinalIgnoreCase),
            RuleMatchType.EndsWith => fieldValue.EndsWith(criteria, StringComparison.OrdinalIgnoreCase),
            RuleMatchType.Regex => TryRegexMatch(fieldValue, criteria),
            _ => false
        };
    }

    /// <summary>
    /// Attempts to retrieve a header value from the headers dictionary.
    /// Returns an empty string when the dictionary is <c>null</c> or the key is not found.
    /// </summary>
    /// <param name="headers">The email headers dictionary.</param>
    /// <param name="key">The header name to look up.</param>
    /// <returns>The header value if found; otherwise <see cref="string.Empty"/>.</returns>
    private static string TryGetHeader(IDictionary<string, string>? headers, string key)
    {
        if (headers is not null && headers.TryGetValue(key, out var value))
            return value;
        return string.Empty;
    }

    /// <summary>
    /// Attempts a case-insensitive regex match with a two-second timeout
    /// to guard against catastrophic backtracking.
    /// </summary>
    /// <param name="input">The string to test.</param>
    /// <param name="pattern">The regular expression pattern.</param>
    /// <returns><c>true</c> if the pattern matches; otherwise <c>false</c>.</returns>
    private bool TryRegexMatch(string input, string pattern)
    {
        try
        {
            return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
        }
        catch (RegexMatchTimeoutException)
        {
            _logger.LogWarning("Regex evaluation timed out for pattern: {Pattern}", pattern);
            return false;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid regex pattern: {Pattern}", pattern);
            return false;
        }
    }
}
