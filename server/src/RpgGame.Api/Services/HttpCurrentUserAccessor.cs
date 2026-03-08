using System.Security.Claims;

namespace RpgGame.Api.Services;

public class HttpCurrentUserAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentUserAccessor
{
    public string? UserId =>
        httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? httpContextAccessor.HttpContext?.User.FindFirst("sub")?.Value;
}
