using RpgGame.Api.Contracts.Hub;

namespace RpgGame.Api.Services;

public interface IEncounterService
{
    Task<EncounterStartedResult> StartEncounterAsync(string joinCode, string userId);
    Task<AddMonsterResult> AddMonsterAsync(string joinCode, string gmUserId, string monsterName);
}
