using Microsoft.AspNetCore.Identity;

namespace RpgGame.Infrastructure.Identity;

public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
}
