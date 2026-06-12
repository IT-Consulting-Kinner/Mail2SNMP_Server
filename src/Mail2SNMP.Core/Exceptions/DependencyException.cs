namespace Mail2SNMP.Core.Exceptions;

/// <summary>
/// Thrown when an entity cannot be deleted because it is still referenced by other entities.
/// The message describes the concrete dependency (e.g., "Mailbox is used by Job 'Critical Alerts'").
/// </summary>
public class DependencyException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance with a message describing the concrete blocking dependency.
    /// </summary>
    /// <param name="message">
    /// Human-readable description of the reference that prevents deletion
    /// (e.g. "Mailbox is used by Job 'Critical Alerts'"). Surfaced to API callers, so it must not leak secrets.
    /// </param>
    public DependencyException(string message) : base(message) { }
}
