namespace Mail2SNMP.Models.Enums;

/// <summary>
/// Represents the processing status of a dead-lettered message.
/// Runtime lifecycle: Pending → Locked → (success: entry deleted) or (failure: back to Pending with backoff).
/// Terminal state: Abandoned (after max attempts or inactive target).
/// </summary>
public enum DeadLetterStatus
{
    /// <summary>The message is waiting to be retried. Initial state and state after a failed retry with remaining attempts.</summary>
    Pending,

    /// <summary>The message is currently locked for reprocessing by a worker instance.</summary>
    Locked,

    /// <summary>The message was permanently abandoned after exceeding the maximum retry attempts or because the target was deactivated.</summary>
    Abandoned
}
