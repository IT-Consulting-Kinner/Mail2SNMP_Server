using Microsoft.AspNetCore.Identity;

namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Application user with ASP.NET Identity integration for cookie-based authentication.
/// </summary>
public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginUtc { get; set; }
    public bool IsActive { get; set; } = true;
}
