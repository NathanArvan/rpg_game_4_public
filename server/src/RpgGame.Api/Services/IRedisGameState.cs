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
}
