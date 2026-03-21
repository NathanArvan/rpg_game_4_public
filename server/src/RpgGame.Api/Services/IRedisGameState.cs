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
    Task SeedCharacterLiveStateAsync(string characterId, int currentHp, string charClass);
    Task<long> DecrementHpAsync(string characterId, long amount);
    Task SetHpAsync(string characterId, long value);
    Task<bool> IsDefeatedAsync(string encounterId, string combatantId);
    Task MarkDefeatedAsync(string encounterId, string combatantId);

    // Planned actions (Hash: combatantId → PlannedAction JSON)
    Task SetPlannedActionAsync(string encounterId, string combatantId, PlannedAction action);
    Task<PlannedAction?> GetPlannedActionAsync(string encounterId, string combatantId);
    Task<Dictionary<string, PlannedAction>> GetAllPlannedActionsAsync(string encounterId);
    Task ClearPlannedActionsAsync(string encounterId);

    // Lock-in (Set of combatant IDs)
    Task AddLockedInAsync(string encounterId, string combatantId);
    Task<bool> IsLockedInAsync(string encounterId, string combatantId);
    Task<long> GetLockedInCountAsync(string encounterId);
    Task<HashSet<string>> GetLockedInSetAsync(string encounterId);
    Task ClearLockedInAsync(string encounterId);
}
