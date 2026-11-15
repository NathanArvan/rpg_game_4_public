using RpgGame.Api.Models;
using StackExchange.Redis;
using System.Text.Json;

namespace RpgGame.Api.Services;

public class RedisGameState : IRedisGameState
{
    private readonly IConnectionMultiplexer _redis;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public RedisGameState(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<Dictionary<string, PlayerEntry>> GetPlayersAsync(string sessionId)
    {
        var db = _redis.GetDatabase();
        var entries = await db.HashGetAllAsync($"session:{sessionId}:players");
        var result = new Dictionary<string, PlayerEntry>();
        foreach (var entry in entries)
        {
            var player = JsonSerializer.Deserialize<PlayerEntry>(entry.Value.ToString()!, _jsonOptions);
            if (player is not null)
                result[entry.Name.ToString()] = player;
        }
        return result;
    }

    public async Task SetPlayerAsync(string sessionId, string userId, PlayerEntry entry)
    {
        var db = _redis.GetDatabase();
        await db.HashSetAsync($"session:{sessionId}:players", userId, JsonSerializer.Serialize(entry, _jsonOptions));
    }

    public async Task<ActiveEncounterEntry?> GetActiveEncounterAsync(string sessionId)
    {
        var db = _redis.GetDatabase();
        var json = await db.StringGetAsync($"session:{sessionId}:active_encounter");
        if (!json.HasValue) return null;
        return JsonSerializer.Deserialize<ActiveEncounterEntry>(json.ToString(), _jsonOptions);
    }

    public async Task SetActiveEncounterAsync(string sessionId, ActiveEncounterEntry entry)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync($"session:{sessionId}:active_encounter", JsonSerializer.Serialize(entry, _jsonOptions));
    }

    public async Task DeleteActiveEncounterAsync(string sessionId)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync($"session:{sessionId}:active_encounter");
    }

    public async Task<MapEntry?> GetMapAsync(string encounterId)
    {
        var db = _redis.GetDatabase();
        var json = await db.StringGetAsync($"encounter:map:{encounterId}");
        if (!json.HasValue) return null;
        return JsonSerializer.Deserialize<MapEntry>(json.ToString(), _jsonOptions);
    }

    public async Task SetMapAsync(string encounterId, MapEntry map)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync($"encounter:map:{encounterId}", JsonSerializer.Serialize(map, _jsonOptions));
    }

    public async Task SeedCharacterLiveStateAsync(string characterId, int currentHp, string charClass)
    {
        var db = _redis.GetDatabase();
        await db.HashSetAsync($"character:live:{characterId}", new HashEntry[]
        {
            new("currentHp", currentHp),
            new("class", charClass),
        });
    }

    public async Task<long> DecrementHpAsync(string characterId, long amount)
    {
        var db = _redis.GetDatabase();
        return await db.HashDecrementAsync($"character:live:{characterId}", "currentHp", amount);
    }

    public async Task SetHpAsync(string characterId, long value)
    {
        var db = _redis.GetDatabase();
        await db.HashSetAsync($"character:live:{characterId}", "currentHp", value);
    }

    public async Task<bool> IsDefeatedAsync(string encounterId, string combatantId)
    {
        var db = _redis.GetDatabase();
        return await db.SetContainsAsync($"encounter:{encounterId}:defeated", combatantId);
    }

    public async Task MarkDefeatedAsync(string encounterId, string combatantId)
    {
        var db = _redis.GetDatabase();
        await db.SetAddAsync($"encounter:{encounterId}:defeated", combatantId);
    }

    // --- Planning phase ---

    public async Task SetPlannedActionAsync(string encounterId, string combatantId, string actionJson)
    {
        var db = _redis.GetDatabase();
        await db.HashSetAsync($"encounter:{encounterId}:planned_actions", combatantId, actionJson);
    }

    public async Task<Dictionary<string, string>> GetAllPlannedActionsAsync(string encounterId)
    {
        var db = _redis.GetDatabase();
        var entries = await db.HashGetAllAsync($"encounter:{encounterId}:planned_actions");
        return entries.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
    }

    public async Task<bool> HasPlannedActionAsync(string encounterId, string combatantId)
    {
        var db = _redis.GetDatabase();
        return await db.HashExistsAsync($"encounter:{encounterId}:planned_actions", combatantId);
    }

    public async Task ClearPlannedActionsAsync(string encounterId)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync($"encounter:{encounterId}:planned_actions");
    }

    // --- Lock-in tracking ---

    public async Task AddLockedInAsync(string encounterId, string combatantId)
    {
        var db = _redis.GetDatabase();
        await db.SetAddAsync($"encounter:{encounterId}:locked_in", combatantId);
    }

    public async Task<bool> IsLockedInAsync(string encounterId, string combatantId)
    {
        var db = _redis.GetDatabase();
        return await db.SetContainsAsync($"encounter:{encounterId}:locked_in", combatantId);
    }

    public async Task<long> GetLockedInCountAsync(string encounterId)
    {
        var db = _redis.GetDatabase();
        return await db.SetLengthAsync($"encounter:{encounterId}:locked_in");
    }

    public async Task AddLockedInBatchAsync(string encounterId, IEnumerable<string> combatantIds)
    {
        var db = _redis.GetDatabase();
        var values = combatantIds.Select(id => (RedisValue)id).ToArray();
        if (values.Length > 0)
            await db.SetAddAsync($"encounter:{encounterId}:locked_in", values);
    }

    public async Task ClearLockedInAsync(string encounterId)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync($"encounter:{encounterId}:locked_in");
    }

    // --- Pending monsters (pre-encounter staging) ---

    public async Task<List<PendingMonster>> GetPendingMonstersAsync(string sessionId)
    {
        var db = _redis.GetDatabase();
        var values = await db.ListRangeAsync($"session:{sessionId}:pending_monsters");
        return values.Select(v => ParsePendingMonster(v.ToString())).ToList();
    }

    public async Task AddPendingMonsterAsync(string sessionId, PendingMonster monster)
    {
        var db = _redis.GetDatabase();
        await db.ListRightPushAsync($"session:{sessionId}:pending_monsters", JsonSerializer.Serialize(monster, _jsonOptions));
    }

    /// <summary>Entries are JSON; a plain name (written before monsters could be AI-controlled) is a manual monster.</summary>
    internal static PendingMonster ParsePendingMonster(string value)
    {
        if (value.StartsWith('{'))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<PendingMonster>(value, _jsonOptions);
                if (parsed is not null) return parsed;
            }
            catch (JsonException) { /* fall through: treat as a name */ }
        }
        return new PendingMonster(value, false);
    }

    public async Task ClearPendingMonstersAsync(string sessionId)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync($"session:{sessionId}:pending_monsters");
    }

    // --- Resolution support ---

    public async Task<int> GetCharacterHpAsync(string characterId)
    {
        var db = _redis.GetDatabase();
        var value = await db.HashGetAsync($"character:live:{characterId}", "currentHp");
        return value.HasValue ? (int)(long)value : 0;
    }

    public async Task StartNextPlanningPhaseAsync(string sessionId, int roundNumber, DateTimeOffset timerExpiresAt)
    {
        var db = _redis.GetDatabase();
        var json = await db.StringGetAsync($"session:{sessionId}:active_encounter");
        if (!json.HasValue) return;
        var entry = JsonSerializer.Deserialize<ActiveEncounterEntry>(json.ToString(), _jsonOptions);
        if (entry is null) return;
        var updated = entry with
        {
            RoundPhase = "planning",
            RoundNumber = roundNumber,
            PlanningTimerExpiresAt = timerExpiresAt,
        };
        await db.StringSetAsync($"session:{sessionId}:active_encounter", JsonSerializer.Serialize(updated, _jsonOptions));
    }
}
