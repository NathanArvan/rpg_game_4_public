using RpgGame.Api.Models;

namespace RpgGame.Api.Services;

public interface IRedisGameState
{
    Task<Dictionary<string, PlayerEntry>> GetPlayersAsync(string sessionId);
    Task SetPlayerAsync(string sessionId, string userId, PlayerEntry entry);
    Task<ActiveEncounterEntry?> GetActiveEncounterAsync(string sessionId);
    Task SetActiveEncounterAsync(string sessionId, ActiveEncounterEntry entry);
    Task DeleteActiveEncounterAsync(string sessionId);
    Task<MapEntry?> GetMapAsync(string encounterId);
    Task SetMapAsync(string encounterId, MapEntry map);
    /// <summary>Writes <c>character:live:{key}</c>: current HP and the combat profile id (values live on the combatant row).</summary>
    Task SeedCharacterLiveStateAsync(string characterId, int currentHp, string profileId);
    Task<long> DecrementHpAsync(string characterId, long amount);
    Task SetHpAsync(string characterId, long value);
    Task<bool> IsDefeatedAsync(string encounterId, string combatantId);
    Task MarkDefeatedAsync(string encounterId, string combatantId);

    // Planning phase
    Task SetPlannedActionAsync(string encounterId, string combatantId, string actionJson);
    Task<Dictionary<string, string>> GetAllPlannedActionsAsync(string encounterId);
    Task<bool> HasPlannedActionAsync(string encounterId, string combatantId);
    Task ClearPlannedActionsAsync(string encounterId);

    // Lock-in tracking
    Task AddLockedInAsync(string encounterId, string combatantId);
    Task<bool> IsLockedInAsync(string encounterId, string combatantId);
    Task<long> GetLockedInCountAsync(string encounterId);
    Task AddLockedInBatchAsync(string encounterId, IEnumerable<string> combatantIds);
    Task ClearLockedInAsync(string encounterId);

    // Pending monsters (pre-encounter staging)
    Task<List<PendingMonster>> GetPendingMonstersAsync(string sessionId);
    Task AddPendingMonsterAsync(string sessionId, PendingMonster monster);
    Task ClearPendingMonstersAsync(string sessionId);

    // Resolution support
    Task<int> GetCharacterHpAsync(string characterId);
    Task StartNextPlanningPhaseAsync(string sessionId, int roundNumber, DateTimeOffset timerExpiresAt);
}
