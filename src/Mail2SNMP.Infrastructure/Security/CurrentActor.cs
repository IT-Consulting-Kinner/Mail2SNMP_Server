using System.Security.Claims;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Models.Enums;
using Microsoft.AspNetCore.Http;

namespace Mail2SNMP.Infrastructure.Security;

/// <summary>
/// Resolves the audit actor from the authenticated principal of the current HTTP request
/// (or Blazor Server circuit). Falls back to the system actor when there is no request —
/// a hosted service running inside a web host, for example.
/// </summary>
/// <remarks>
/// API keys are reported as <see cref="ActorType.Service"/> with an <c>apikey:&lt;name&gt;</c>
/// identifier, so an automated change is distinguishable from a human one in the audit log
/// without exposing the key itself.
/// </remarks>
public sealed class HttpContextCurrentActor : ICurrentActor
{
    private readonly IHttpContextAccessor _accessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpContextCurrentActor"/> class.
    /// </summary>
    /// <param name="accessor">Accessor for the ambient <see cref="HttpContext"/>, if any.</param>
    public HttpContextCurrentActor(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal
    {
        get
        {
            var user = _accessor.HttpContext?.User;
            return user?.Identity?.IsAuthenticated == true ? user : null;
        }
    }

    /// <inheritdoc />
    public ActorType Type
    {
        get
        {
            var principal = Principal;
            if (principal is null) return ActorType.System;
            // The API-key handler stamps this claim; a cookie/OIDC principal does not.
            return principal.HasClaim(c => c.Type == "ApiKeyId") ? ActorType.Service : ActorType.User;
        }
    }

    /// <inheritdoc />
    public string Id
    {
        get
        {
            var principal = Principal;
            if (principal is null) return SystemCurrentActor.SystemId;

            var name = principal.FindFirstValue(ClaimTypes.Name)
                       ?? principal.FindFirstValue(ClaimTypes.Email)
                       ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(name))
                return "unknown";

            // Label API keys so an automated change reads as such. The key's *name* is
            // safe to record; its value never reaches this class.
            return principal.HasClaim(c => c.Type == "ApiKeyId") ? $"apikey:{name}" : name;
        }
    }
}

/// <summary>
/// The actor used where there genuinely is no user: the worker's background services and
/// the CLI.
/// </summary>
public sealed class SystemCurrentActor : ICurrentActor
{
    /// <summary>The conventional identifier recorded for system-initiated operations.</summary>
    public const string SystemId = "system";

    private readonly string _id;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemCurrentActor"/> class.
    /// </summary>
    /// <param name="id">
    /// Identifier to record, defaulting to <see cref="SystemId"/>. The CLI passes something
    /// more specific (e.g. <c>cli:DOMAIN\user</c>) so an operator-run command is not
    /// indistinguishable from a scheduled background change.
    /// </param>
    public SystemCurrentActor(string? id = null) => _id = string.IsNullOrWhiteSpace(id) ? SystemId : id;

    /// <inheritdoc />
    public ActorType Type => ActorType.System;

    /// <inheritdoc />
    public string Id => _id;
}
