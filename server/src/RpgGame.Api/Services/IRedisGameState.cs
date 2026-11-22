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
    /// <summary>Back on their feet (3B.3): removes the combatant from the defeated set and its status entry.</summary>
    Task ReviveAsync(string encounterId, string combatantId);
    /// <summary>Downed / stable / dead detail for a defeated combatant (3B.5); null if none was stored.</summary>
    Task<CombatantStatusEntry?> GetStatusAsync(string encounterId, string combatantId);
    Task SetStatusAsync(string encounterId, string combatantId, CombatantStatusEntry status);
    /// <summary>Abilities on cooldown for one combatant (3B.3): ability id → rounds left. Empty when none.</summary>
    Task<Dictionary<string, int>> GetCooldownsAsync(string encounterId, string combatantId);
    /// <summary>Replaces one combatant's cooldowns; an empty map removes the entry.</summary>
    Task SetCooldownsAsync(string encounterId, string combatantId, IReadOnlyDictionary<string, int> cooldowns);

    // Planning phase
    Task SetPlannedActionAsync(string encounterId, string combatantId, string actionJson);
    /// <summary>Removes one combatant's plan (an empty plan from the client: "plan nothing yet").</summary>
    Task ClearPlannedActionAsync(string encounterId, string combatantId);
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
