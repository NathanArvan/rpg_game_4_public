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
        return JsonSerializer.Deserialize<ActiveEncounterEntry>(json!, _jsonOptions);
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
        return JsonSerializer.Deserialize<MapEntry>(json!, _jsonOptions);
    }

    public async Task SetMapAsync(string encounterId, MapEntry map)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync($"encounter:map:{encounterId}", JsonSerializer.Serialize(map, _jsonOptions));
    }

    public async Task SeedCharacterLiveStateAsync(string characterId, int currentHp, int attackBonus, int damageBase, int dodgeChance, int maxAp, int movementRange)
    {
        var db = _redis.GetDatabase();
        await db.HashSetAsync($"character:live:{characterId}", new HashEntry[]
        {
            new("currentHp",     currentHp),
            new("attackBonus",   attackBonus),
            new("damageBase",    damageBase),
            new("dodgeChance",   dodgeChance),
            new("maxAp",         maxAp),
            new("movementRange", movementRange),
        });
    }

    public async Task<(int currentHp, int attackBonus, int damageBase, int dodgeChance, int maxAp, int movementRange)> GetCharacterLiveStateAsync(string characterId)
    {
        var db = _redis.GetDatabase();
        var entries = await db.HashGetAllAsync($"character:live:{characterId}");
        int currentHp = 0, attackBonus = 0, damageBase = 2, dodgeChance = 0, maxAp = 2, movementRange = 5;
        foreach (var entry in entries)
        {
            switch (entry.Name.ToString())
            {
                case "currentHp":     currentHp     = (int)entry.Value; break;
                case "attackBonus":   attackBonus   = (int)entry.Value; break;
                case "damageBase":    damageBase    = (int)entry.Value; break;
                case "dodgeChance":   dodgeChance   = (int)entry.Value; break;
                case "maxAp":         maxAp         = (int)entry.Value; break;
                case "movementRange": movementRange = (int)entry.Value; break;
            }
        }
        return (currentHp, attackBonus, damageBase, dodgeChance, maxAp, movementRange);
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

    // Planned actions
    public async Task SetPlannedActionAsync(string encounterId, string combatantId, PlannedAction action)
    {
        var db = _redis.GetDatabase();
        await db.HashSetAsync($"encounter:{encounterId}:planned_actions", combatantId,
            JsonSerializer.Serialize(action, _jsonOptions));
    }

    public async Task<PlannedAction?> GetPlannedActionAsync(string encounterId, string combatantId)
    {
        var db = _redis.GetDatabase();
        var json = await db.HashGetAsync($"encounter:{encounterId}:planned_actions", combatantId);
        if (!json.HasValue) return null;
        return JsonSerializer.Deserialize<PlannedAction>(json!, _jsonOptions);
    }

    public async Task<Dictionary<string, PlannedAction>> GetAllPlannedActionsAsync(string encounterId)
    {
        var db = _redis.GetDatabase();
        var entries = await db.HashGetAllAsync($"encounter:{encounterId}:planned_actions");
        var result = new Dictionary<string, PlannedAction>();
        foreach (var entry in entries)
        {
            var action = JsonSerializer.Deserialize<PlannedAction>(entry.Value.ToString()!, _jsonOptions);
            if (action is not null)
                result[entry.Name.ToString()] = action;
        }
        return result;
    }

    public async Task ClearPlannedActionsAsync(string encounterId)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync($"encounter:{encounterId}:planned_actions");
    }

    // Lock-in
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

    public async Task<HashSet<string>> GetLockedInSetAsync(string encounterId)
    {
        var db = _redis.GetDatabase();
        var members = await db.SetMembersAsync($"encounter:{encounterId}:locked_in");
        return members.Select(m => m.ToString()).ToHashSet();
    }

    public async Task ClearLockedInAsync(string encounterId)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync($"encounter:{encounterId}:locked_in");
    }
}
