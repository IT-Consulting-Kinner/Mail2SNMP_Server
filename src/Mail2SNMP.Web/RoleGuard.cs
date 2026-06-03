using Microsoft.AspNetCore.Components.Authorization;

namespace Mail2SNMP.Web;

/// <summary>
/// V1: Server-side role enforcement for Blazor Server interactive pages.
///
/// The REST API enforces authorization at every endpoint, but the Blazor UI
/// calls the service layer directly and the services perform no authorization.
/// Several write-capable pages previously had no <c>[Authorize]</c> attribute,
/// so the fallback policy (authenticated-only) let a ReadOnly user create,
/// edit and delete configuration through the UI — a privilege escalation the
/// API does not permit.
///
/// <see cref="AuthorizeView"/> hides buttons in the rendered tree, which in
/// Blazor Server is itself a real control (an un-rendered button has no event
/// handler the client can invoke). This helper adds the defence-in-depth layer:
/// every mutating handler re-checks the caller's role server-side before
/// touching a service, so the protection does not rely solely on a button not
/// being rendered.
/// </summary>
public static class RoleGuard
{
    /// <summary>
    /// Returns true if the current user is authenticated and holds at least one
    /// of the supplied roles. The cascading <see cref="AuthenticationState"/> is
    /// supplied by <c>CascadingAuthenticationState</c> (registered via
    /// <c>AddCascadingAuthenticationState</c>).
    /// </summary>
    public static async Task<bool> HasAnyRoleAsync(Task<AuthenticationState>? authState, params string[] roles)
    {
        if (authState is null) return false;
        var state = await authState;
        var user = state.User;
        if (user.Identity?.IsAuthenticated != true) return false;
        foreach (var role in roles)
            if (user.IsInRole(role)) return true;
        return false;
    }
}
