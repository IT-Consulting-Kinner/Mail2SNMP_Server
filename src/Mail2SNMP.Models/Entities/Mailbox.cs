using System.ComponentModel.DataAnnotations;

namespace Mail2SNMP.Models.Entities;

/// <summary>
/// IMAP mailbox configuration for email polling. Credentials are stored encrypted using AES-256-GCM.
/// </summary>
public class Mailbox
{
    /// <summary>Surrogate primary key. Database-generated identity; zero on a not-yet-persisted instance.</summary>
    public int Id { get; set; }

    /// <summary>Operator-facing display name for the mailbox. Required, 1–200 characters.</summary>
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Name must be between 1 and 200 characters.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Hostname or IP address of the IMAP server. Required, 1–500 characters.</summary>
    [Required(ErrorMessage = "Host is required.")]
    [StringLength(500, MinimumLength = 1, ErrorMessage = "Host must be between 1 and 500 characters.")]
    public string Host { get; set; } = string.Empty;

    /// <summary>IMAP server port. Range 1–65535; defaults to 993, the standard IMAPS (implicit-TLS) port.</summary>
    [Range(1, 65535, ErrorMessage = "Port must be between 1 and 65535.")]
    public int Port { get; set; } = 993;

    /// <summary>Whether to connect using SSL/TLS. Defaults to <c>true</c>; disable only for plaintext/STARTTLS-less servers.</summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>IMAP account login (username). Required, 1–500 characters. Plaintext; not encrypted.</summary>
    [Required(ErrorMessage = "Username is required.")]
    [StringLength(500, MinimumLength = 1, ErrorMessage = "Username must be between 1 and 500 characters.")]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// AES-256-GCM <b>ciphertext</b> of the IMAP account password (never plaintext at rest).
    /// Required; the <c>Required</c> attribute guards against an empty value being persisted.
    /// </summary>
    [Required(ErrorMessage = "Password is required.")]
    public string EncryptedPassword { get; set; } = string.Empty;

    /// <summary>IMAP folder to poll. Required, 1–500 characters; defaults to <c>"INBOX"</c>.</summary>
    [Required(ErrorMessage = "Folder is required.")]
    [StringLength(500, MinimumLength = 1, ErrorMessage = "Folder must be between 1 and 500 characters.")]
    public string Folder { get; set; } = "INBOX";

    /// <summary>Whether the mailbox is eligible for polling. Inactive mailboxes are skipped by the worker. Defaults to <c>true</c>.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>UTC timestamp recorded when the mailbox was created. Defaults to the current UTC time.</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the most recent poll attempt (successful or not); <c>null</c> if the mailbox has never been polled.</summary>
    public DateTime? LastCheckedUtc { get; set; }

    /// <summary>Message from the most recent failed poll (e.g. auth or connection error); <c>null</c> when the last poll succeeded.</summary>
    public string? LastError { get; set; }

    /// <summary>EF Core optimistic-concurrency token; updated by the database on each row change to detect lost updates.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    /// <summary>Jobs that poll this mailbox. Lazy-loaded navigation collection (inverse of <see cref="Job.MailboxId"/>).</summary>
    public ICollection<Job> Jobs { get; set; } = new List<Job>();
}
