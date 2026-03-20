using System.ComponentModel.DataAnnotations;

namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Webhook target configuration for HTTP POST notification delivery with optional HMAC-SHA256 signing.
/// </summary>
public class WebhookTarget
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "URL is required.")]
    [Url(ErrorMessage = "Please enter a valid URL.")]
    public string Url { get; set; } = string.Empty;

    public string? Headers { get; set; }
    public string? PayloadTemplate { get; set; }
    public string? EncryptedSecret { get; set; }

    [Range(1, 10000, ErrorMessage = "Max requests per minute must be between 1 and 10,000.")]
    public int MaxRequestsPerMinute { get; set; } = 60;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
