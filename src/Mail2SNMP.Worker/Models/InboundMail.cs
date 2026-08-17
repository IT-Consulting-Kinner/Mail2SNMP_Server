using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Worker.Models;

/// <summary>
/// A single inbound message reduced to the fields the processing pipeline needs,
/// already truncated to the database's column limits.
/// </summary>
/// <remarks>
/// Deliberately free of any MailKit type. The processing decisions — claim, rule
/// evaluation, rate limiting, event creation, disposition and notification — used to be
/// inlined in the IMAP fetch loop, which made them reachable only through a live IMAP
/// server and therefore untestable. That is where the per-job claim defect (H-1), the
/// rate-limit accounting defect (H-2) and the abandoned-claim defect (H-4) all lived.
/// Reducing the message to this record at the IMAP boundary is what lets
/// <see cref="Services.MailProcessingPipeline"/> be exercised directly.
/// </remarks>
/// <param name="ClaimKey">
/// The stable idempotency key. Normally the RFC 5322 <c>Message-ID</c>; RFC 5322 makes
/// that header optional, so mails without one get a deterministic synthetic key derived
/// from the IMAP UID and internal date, which every cluster node computes identically.
/// </param>
/// <param name="MessageId">The raw <c>Message-ID</c> header, or <c>null</c> when absent.</param>
/// <param name="From">Sender address, truncated to the column limit.</param>
/// <param name="Subject">Subject line, truncated to the column limit.</param>
/// <param name="Body">Text (or HTML fallback) body, bounded for rule matching.</param>
/// <param name="ReceivedUtc">The message's own date header, in UTC.</param>
/// <param name="Headers">
/// All headers, keyed case-insensitively, for header-field rules. Typed as
/// <see cref="IDictionary{TKey,TValue}"/> to match <c>RuleEvaluator.Evaluate</c> so the
/// per-mail hot path does not copy the dictionary.
/// </param>
public sealed record InboundMail(
    string ClaimKey,
    string? MessageId,
    string From,
    string Subject,
    string Body,
    DateTime ReceivedUtc,
    IDictionary<string, string> Headers);

/// <summary>
/// What the pipeline did with one inbound mail.
/// </summary>
/// <param name="Disposition">
/// The recorded outcome, or <c>null</c> when the mail was skipped because another job
/// instance had already completed it (a duplicate claim).
/// </param>
/// <param name="EventId">The event created or deduplicated into, when there was one.</param>
/// <param name="MarkedSeen">
/// Whether the mail was flagged <c>Seen</c> on the server. <c>false</c> means at least one
/// other active job on the mailbox has not finished with it yet, so the flag is deferred —
/// flagging it early would hide the mail from the sibling jobs' <c>NotSeen</c> search.
/// </param>
public sealed record MailProcessingOutcome(
    MailDisposition? Disposition,
    long? EventId,
    bool MarkedSeen);
