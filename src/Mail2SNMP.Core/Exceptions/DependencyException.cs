namespace Mail2SNMP.Core.Exceptions;

/// <summary>
/// Thrown when an entity cannot be deleted because it is still referenced by other entities.
/// The message describes the concrete dependency (e.g., "Mailbox is used by Job 'Critical Alerts'").
/// </summary>
public class DependencyException : InvalidOperationException
{
    public DependencyException(string message) : base(message) { }
}
