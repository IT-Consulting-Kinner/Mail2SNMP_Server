using System.ComponentModel.DataAnnotations;

namespace Mail2SNMP.Models.Entities;

/// <summary>
/// IMAP mailbox configuration for email polling. Credentials are stored encrypted using AES-256-GCM.
/// </summary>
public class Mailbox
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Host is required.")]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535, ErrorMessage = "Port must be between 1 and 65535.")]
    public int Port { get; set; } = 993;

    public bool UseSsl { get; set; } = true;

    [Required(ErrorMessage = "Username is required.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    public string EncryptedPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Folder is required.")]
    public string Folder { get; set; } = "INBOX";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastCheckedUtc { get; set; }
    public string? LastError { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<Job> Jobs { get; set; } = new List<Job>();
}
