using Microsoft.AspNetCore.Identity;

namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Application user with ASP.NET Identity integration for cookie-based authentication.
/// </summary>
public class AppUser : IdentityUser
{
    /// <summary>Human-friendly name shown in the UI, distinct from the Identity <c>UserName</c>/email login.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>UTC time the account was created. Defaults to <see cref="DateTime.UtcNow"/> at construction.</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>UTC time of the user's most recent successful sign-in, or <c>null</c> if they have never logged in.</summary>
    public DateTime? LastLoginUtc { get; set; }

    /// <summary>
    /// Whether the account may sign in. Defaults to <c>true</c>; set <c>false</c> to disable the user
    /// without deleting the record (preserving audit history and foreign-key references).
    /// </summary>
    public bool IsActive { get; set; } = true;
}
