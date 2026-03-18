using RpgGame.Api.Contracts.Hub;

namespace RpgGame.Api.Services;

public interface IEncounterService
{
    Task<EncounterStartedResult> StartEncounterAsync(string joinCode, string userId);
    Task<TokenMovedResult> MoveTokenAsync(string joinCode, string userId, string combatantId, int targetX, int targetY);
    Task<EndTurnResult> EndTurnAsync(string joinCode, string userId);
}
