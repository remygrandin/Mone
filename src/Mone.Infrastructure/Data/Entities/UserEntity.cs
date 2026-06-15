using Microsoft.AspNetCore.Identity;

namespace Mone.Infrastructure.Data.Entities;

public class UserEntity : IdentityUser
{
    public string? AuthProvider { get; set; }
    public string? ExternalId { get; set; }
    public string? DisplayName { get; set; }
    public string? ThemePreference { get; set; }
}
