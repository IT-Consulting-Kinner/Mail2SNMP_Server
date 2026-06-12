using System.ComponentModel.DataAnnotations;

namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Webhook target configuration for HTTP POST notification delivery with optional HMAC-SHA256 signing.
/// </summary>
public class WebhookTarget
{
    /// <summary>Surrogate primary key. Identity column assigned by the database.</summary>
    public int Id { get; set; }

    /// <summary>Operator-facing target name shown in the management UI. Required, 1–200 characters.</summary>
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Name must be between 1 and 200 characters.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Absolute HTTP(S) endpoint that notifications are POSTed to. Required, must be a valid URL, 1–2000 characters.
    /// </summary>
    [Required(ErrorMessage = "URL is required.")]
    [Url(ErrorMessage = "Please enter a valid URL.")]
    [StringLength(2000, MinimumLength = 1, ErrorMessage = "URL must be between 1 and 2000 characters.")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Optional additional request headers as a JSON object (header-name to value). Sent on every POST;
    /// <c>null</c> means no custom headers. Max 4000 characters.
    /// </summary>
    [StringLength(4000, ErrorMessage = "Headers JSON must not exceed 4000 characters.")]
    public string? Headers { get; set; }

    /// <summary>
    /// Optional template used to render the POST body (with event placeholders). When <c>null</c>,
    /// the default payload format is used. Max 8000 characters.
    /// </summary>
    [StringLength(8000, ErrorMessage = "Payload template must not exceed 8000 characters.")]
    public string? PayloadTemplate { get; set; }

    /// <summary>
    /// Encrypted HMAC signing secret. Holds ciphertext at rest (data-protection encrypted), not the
    /// plaintext secret. When set, outgoing requests are signed with HMAC-SHA256 over the body so the
    /// receiver can verify authenticity. <c>null</c> means request signing is disabled.
    /// </summary>
    public string? EncryptedSecret { get; set; }

    /// <summary>
    /// Client-side rate limit, in requests per minute, applied to deliveries to this target.
    /// Must be between 1 and 10,000; defaults to 60.
    /// </summary>
    [Range(1, 10000, ErrorMessage = "Max requests per minute must be between 1 and 10,000.")]
    public int MaxRequestsPerMinute { get; set; } = 60;

    /// <summary>Whether the target receives deliveries. Defaults to <c>true</c>; inactive targets are skipped.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>UTC timestamp when the target was created. Defaults to <see cref="DateTime.UtcNow"/> at construction.</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// SQL Server <c>rowversion</c> concurrency token. Updated automatically on every save and
    /// used for optimistic concurrency checks; do not set manually.
    /// </summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
