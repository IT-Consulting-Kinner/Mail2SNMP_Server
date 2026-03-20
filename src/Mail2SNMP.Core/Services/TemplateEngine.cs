using System.Text.RegularExpressions;
using Mail2SNMP.Models.DTOs;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Core.Services;

/// <summary>
/// Template engine for rendering notification payloads using placeholder substitution.
/// Placeholders follow the <c>{Name}</c> syntax and are resolved from a
/// <see cref="NotificationContext"/> instance.
/// </summary>
public class TemplateEngine
{
    private static readonly Regex PlaceholderRegex = new(@"\{(\w+)\}", RegexOptions.Compiled);
    private const int MaxFieldLength = 500;
    private const int MaxSnmpOctetStringBytes = 255;

    private readonly ILogger<TemplateEngine> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TemplateEngine"/> class.
    /// </summary>
    /// <param name="logger">Logger used to record warnings about unresolved placeholders.</param>
    public TemplateEngine(ILogger<TemplateEngine> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Builds a key/value payload dictionary from the notification context.
    /// When a <paramref name="template"/> is supplied, only the placeholders
    /// referenced in the template are included in the result; otherwise all
    /// available values are returned.
    /// </summary>
    /// <param name="context">The notification context containing event and email metadata.</param>
    /// <param name="template">
    /// An optional template string whose <c>{Placeholder}</c> tokens determine
    /// which keys appear in the returned dictionary. Pass <c>null</c> to include all values.
    /// </param>
    /// <returns>A dictionary mapping placeholder names to their resolved values.</returns>
    public Dictionary<string, object> BuildPayload(NotificationContext context, string? template = null)
    {
        var values = GetPlaceholderValues(context);

        if (string.IsNullOrWhiteSpace(template))
        {
            return values.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
        }

        var result = new Dictionary<string, object>();
        foreach (var match in PlaceholderRegex.Matches(template).Cast<Match>())
        {
            var key = match.Groups[1].Value;
            if (values.TryGetValue(key, out var value))
            {
                result[key] = value;
            }
            else
            {
                _logger.LogWarning("Unknown template placeholder: {{{Placeholder}}}", key);
                result[key] = match.Value;
            }
        }

        return result.Count > 0 ? result : values.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
    }

    /// <summary>
    /// Renders a template string by replacing all <c>{Placeholder}</c> tokens with
    /// their corresponding values from the notification context.
    /// Unrecognized placeholders are left in place and a warning is logged.
    /// </summary>
    /// <param name="context">The notification context containing event and email metadata.</param>
    /// <param name="template">The template string containing <c>{Placeholder}</c> tokens.</param>
    /// <returns>The fully rendered string with placeholders replaced by their values.</returns>
    public string RenderTemplate(NotificationContext context, string template)
    {
        var values = GetPlaceholderValues(context);
        return PlaceholderRegex.Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            if (values.TryGetValue(key, out var value))
            {
                return value;
            }
            _logger.LogWarning("Unknown template placeholder: {{{Placeholder}}}", key);
            return match.Value;
        });
    }

    /// <summary>
    /// Truncates a UTF-8 string so that it does not exceed the SNMP OCTET STRING
    /// maximum of 255 bytes. Multi-byte characters at the truncation boundary are
    /// handled to avoid producing an invalid string.
    /// </summary>
    /// <param name="value">The string to truncate.</param>
    /// <returns>The original string if it fits, or a truncated version within the 255-byte limit.</returns>
    public string TruncateForSnmp(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        if (bytes.Length <= MaxSnmpOctetStringBytes)
            return value;

        var truncated = new byte[MaxSnmpOctetStringBytes];
        Array.Copy(bytes, truncated, MaxSnmpOctetStringBytes);
        // Ensure we don't split a multi-byte character
        var result = System.Text.Encoding.UTF8.GetString(truncated);
        // Remove potential broken char at the end
        if (result.Length > 0 && char.IsHighSurrogate(result[^1]))
            result = result[..^1];
        return result;
    }

    /// <summary>
    /// Extracts all available placeholder values from the notification context
    /// into a case-insensitive dictionary. Long field values are truncated to
    /// <see cref="MaxFieldLength"/> characters.
    /// </summary>
    /// <param name="context">The notification context to extract values from.</param>
    /// <returns>A dictionary of placeholder names to their string values.</returns>
    private Dictionary<string, string> GetPlaceholderValues(NotificationContext context)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["JobName"] = context.JobName,
            ["Mailbox"] = context.Mailbox,
            ["From"] = Truncate(context.From, MaxFieldLength),
            ["Subject"] = Truncate(context.Subject, MaxFieldLength),
            ["Severity"] = context.Severity.ToString(),
            ["EventId"] = context.EventId.ToString(),
            ["TimestampUtc"] = context.TimestampUtc.ToString("O"),
            ["HitCount"] = context.HitCount.ToString(),
            ["RuleName"] = context.RuleName
        };
    }

    /// <summary>
    /// Truncates a string to the specified maximum length, appending an ellipsis
    /// when truncation occurs.
    /// </summary>
    /// <param name="value">The string to truncate.</param>
    /// <param name="maxLength">The maximum allowed length including the ellipsis.</param>
    /// <returns>The original or truncated string.</returns>
    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value ?? string.Empty;
        return value[..(maxLength - 3)] + "...";
    }
}
