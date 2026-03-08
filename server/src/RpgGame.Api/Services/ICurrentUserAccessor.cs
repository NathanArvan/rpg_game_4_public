namespace RpgGame.Api.Services;

public interface ICurrentUserAccessor
{
    string? UserId { get; }
}
