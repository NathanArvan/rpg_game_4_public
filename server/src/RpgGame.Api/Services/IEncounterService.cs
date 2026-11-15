using RpgGame.Api.Contracts.Hub;

namespace RpgGame.Api.Services;

public interface IEncounterService
{
    Task<EncounterStartedResult> StartEncounterAsync(string joinCode, string userId);
    Task<AddMonsterResult> AddMonsterAsync(string joinCode, string gmUserId, string monsterName);

    /// <summary>
    /// Marks the encounter Ended, logs an "encounter_ended" GameEvent, and clears the session's live
    /// encounter state in Redis so the session returns to the lobby (a new encounter can be started).
    /// </summary>
    Task EndEncounterAsync(Guid encounterId, string winningSide, int finalRoundNumber);
}
