using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Core.Interfaces;

/// <summary>
/// Resolves who is responsible for the operation currently being performed, so audit
/// entries can name them.
/// </summary>
/// <remarks>
/// <para>
/// Every configuration mutation used to be audited as <see cref="ActorType.System"/> /
/// <c>"system"</c>, regardless of who actually triggered it. The audit log therefore
/// recorded that a job was deleted but never by whom — which is the one question an audit
/// trail exists to answer. Login and event-lifecycle actions already carried a real actor;
/// configuration changes did not.
/// </para>
/// <para>
/// Implementations are per-scope: in a request-serving host the actor comes from the
/// authenticated principal, and in the worker and CLI it is genuinely the system.
/// </para>
/// </remarks>
public interface ICurrentActor
{
    /// <summary>The kind of actor responsible for the current operation.</summary>
    ActorType Type { get; }

    /// <summary>
    /// A stable, human-recognizable identifier for the actor — a user name, an
    /// <c>apikey:&lt;name&gt;</c> label, or a description of the background context.
    /// Never a credential.
    /// </summary>
    string Id { get; }
}
