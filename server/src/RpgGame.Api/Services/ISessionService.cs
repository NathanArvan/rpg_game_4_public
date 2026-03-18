using RpgGame.Api.Contracts.Hub;

namespace RpgGame.Api.Services;

public interface ISessionService
{
    Task<JoinSessionResult> JoinSessionAsync(string joinCode, string userId, string displayName, string? characterId);
}
