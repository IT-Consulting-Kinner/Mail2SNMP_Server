using System.Security.Claims;
using Mail2SNMP.Web;
using Microsoft.AspNetCore.Components.Authorization;

namespace Mail2SNMP.Tests.Web;

/// <summary>
/// Peer-review: RoleGuard is the server-side defence-in-depth check every
/// mutating Blazor handler runs before touching a service. It was structurally
/// untestable until the test project referenced Mail2SNMP.Web.
/// </summary>
public class RoleGuardTests
{
    private static Task<AuthenticationState> State(ClaimsPrincipal principal) =>
        Task.FromResult(new AuthenticationState(principal));

    private static ClaimsPrincipal Authenticated(params string[] roles)
    {
        // An authenticationType makes Identity.IsAuthenticated == true.
        var identity = new ClaimsIdentity(
            roles.Select(r => new Claim(ClaimTypes.Role, r)), authenticationType: "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    [Fact]
    public async Task NullAuthState_ReturnsFalse()
    {
        Assert.False(await RoleGuard.HasAnyRoleAsync(null, "Admin"));
    }

    [Fact]
    public async Task AnonymousUser_ReturnsFalse()
    {
        Assert.False(await RoleGuard.HasAnyRoleAsync(State(Anonymous()), "Admin", "Operator"));
    }

    [Fact]
    public async Task UserInRole_ReturnsTrue()
    {
        Assert.True(await RoleGuard.HasAnyRoleAsync(State(Authenticated("Admin")), "Admin"));
    }

    [Fact]
    public async Task UserInOneOfSeveralRoles_ReturnsTrue()
    {
        Assert.True(await RoleGuard.HasAnyRoleAsync(State(Authenticated("Operator")), "Admin", "Operator"));
    }

    [Fact]
    public async Task ReadOnlyUser_AskedForWriteRoles_ReturnsFalse()
    {
        // The V1 privilege-escalation case: a ReadOnly user must not pass an
        // Admin/Operator gate.
        Assert.False(await RoleGuard.HasAnyRoleAsync(State(Authenticated("ReadOnly")), "Admin", "Operator"));
    }
}
