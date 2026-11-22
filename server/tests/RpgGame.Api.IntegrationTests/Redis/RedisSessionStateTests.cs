using RpgGame.Api.Models;
using StackExchange.Redis;

namespace RpgGame.Api.IntegrationTests.Redis;

/// <summary>
/// Session-level state: connected players, the active encounter, the map and monsters staged in
/// the lobby. Each test round-trips a read/write pair and pins the key name and Redis type from
/// the "Redis key patterns" table in CLAUDE.md (live data and redis-cli users depend on them).
/// </summary>
[Collection(RedisCollection.Name)]
public class RedisSessionStateTests(RedisFixture redis)
{
    private readonly IDatabase _db = redis.Db;

    [Fact]
    public async Task Players_round_trip_in_the_session_players_hash()
    {
        var sessionId = RedisFixture.NewId();

        await redis.State.SetPlayerAsync(sessionId, "user-1", new PlayerEntry("Ann", "char-1"));
        await redis.State.SetPlayerAsync(sessionId, "user-2", new PlayerEntry("Bo", "char-2"));
        await redis.State.SetPlayerAsync(sessionId, "user-1", new PlayerEntry("Ann B", "char-3")); // replaces

        var players = await redis.State.GetPlayersAsync(sessionId);

        Assert.Equal(2, players.Count);
        Assert.Equal(new PlayerEntry("Ann B", "char-3"), players["user-1"]);
        Assert.Equal(new PlayerEntry("Bo", "char-2"), players["user-2"]);
        Assert.Equal(RedisType.Hash, await _db.KeyTypeAsync($"session:{sessionId}:players"));
    }

    [Fact]
    public async Task No_players_reads_as_empty()
    {
        Assert.Empty(await redis.State.GetPlayersAsync(RedisFixture.NewId()));
    }

    [Fact]
    public async Task Active_encounter_round_trips_and_is_deleted()
    {
        var sessionId = RedisFixture.NewId();
        var expires = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
        var entry = new ActiveEncounterEntry("enc-1", "planning", 1, expires,
            [new CombatantRef("c1", "u1"), new CombatantRef("c2", "gm", "monster", IsAiControlled: true)],
            PlanningTimerEnabled: false);

        Assert.Null(await redis.State.GetActiveEncounterAsync(sessionId));
        await redis.State.SetActiveEncounterAsync(sessionId, entry);

        var read = await redis.State.GetActiveEncounterAsync(sessionId);
        Assert.NotNull(read);
        Assert.Equal(entry with { Combatants = read!.Combatants }, read);
        Assert.Equal(entry.Combatants, read.Combatants);
        Assert.Equal(RedisType.String, await _db.KeyTypeAsync($"session:{sessionId}:active_encounter"));

        await redis.State.DeleteActiveEncounterAsync(sessionId);
        Assert.Null(await redis.State.GetActiveEncounterAsync(sessionId));
        Assert.False(await _db.KeyExistsAsync($"session:{sessionId}:active_encounter"));
    }

    [Fact]
    public async Task Active_encounter_is_stored_as_camel_case_json()
    {
        var sessionId = RedisFixture.NewId();
        await redis.State.SetActiveEncounterAsync(sessionId,
            new ActiveEncounterEntry("enc-1", "planning", 3, DateTimeOffset.UnixEpoch, [new CombatantRef("c1", "u1")]));

        var json = (string)(await _db.StringGetAsync($"session:{sessionId}:active_encounter"))!;

        Assert.Contains("\"encounterId\":\"enc-1\"", json);
        Assert.Contains("\"roundPhase\":\"planning\"", json);
        Assert.Contains("\"roundNumber\":3", json);
        Assert.Contains("\"combatantType\":\"player\"", json);
    }

    [Fact]
    public async Task Start_next_planning_phase_updates_round_and_timer_and_keeps_the_rest()
    {
        var sessionId = RedisFixture.NewId();
        var combatants = new List<CombatantRef> { new("c1", "u1") };
        await redis.State.SetActiveEncounterAsync(sessionId,
            new ActiveEncounterEntry("enc-1", "resolving", 1, DateTimeOffset.UnixEpoch, combatants, PlanningTimerEnabled: false));
        var expires = new DateTimeOffset(2026, 11, 22, 12, 5, 0, TimeSpan.Zero);

        await redis.State.StartNextPlanningPhaseAsync(sessionId, 2, expires);

        var read = (await redis.State.GetActiveEncounterAsync(sessionId))!;
        Assert.Equal("planning", read.RoundPhase);
        Assert.Equal(2, read.RoundNumber);
        Assert.Equal(expires, read.PlanningTimerExpiresAt);
        Assert.Equal("enc-1", read.EncounterId);
        Assert.False(read.PlanningTimerEnabled);
        Assert.Equal(combatants, read.Combatants);
    }

    [Fact]
    public async Task Start_next_planning_phase_without_an_encounter_writes_nothing()
    {
        var sessionId = RedisFixture.NewId();

        await redis.State.StartNextPlanningPhaseAsync(sessionId, 2, DateTimeOffset.UnixEpoch);

        Assert.False(await _db.KeyExistsAsync($"session:{sessionId}:active_encounter"));
    }

    [Fact]
    public async Task Map_round_trips_under_encounter_map_key()
    {
        var encounterId = RedisFixture.NewId();
        var map = new MapEntry(20, 20, new Dictionary<string, Position> { ["c1"] = new(3, 4), ["c2"] = new(19, 0) }, ["0,0"]);

        Assert.Null(await redis.State.GetMapAsync(encounterId));
        await redis.State.SetMapAsync(encounterId, map);

        var read = (await redis.State.GetMapAsync(encounterId))!;
        Assert.Equal(20, read.GridWidth);
        Assert.Equal(20, read.GridHeight);
        Assert.Equal(map.Positions, read.Positions);
        Assert.Equal(map.FogOfWar, read.FogOfWar);
        Assert.Equal(RedisType.String, await _db.KeyTypeAsync($"encounter:map:{encounterId}"));
    }

    [Fact]
    public async Task Pending_monsters_keep_order_and_profile_and_are_cleared()
    {
        var sessionId = RedisFixture.NewId();

        await redis.State.AddPendingMonsterAsync(sessionId, new PendingMonster("Goblin", false, "grunt"));
        await redis.State.AddPendingMonsterAsync(sessionId, new PendingMonster("Ogre", true, "brute"));
        await redis.State.AddPendingMonsterAsync(sessionId, new PendingMonster("Rat", false));

        Assert.Equal(
            [new PendingMonster("Goblin", false, "grunt"), new PendingMonster("Ogre", true, "brute"), new PendingMonster("Rat", false)],
            await redis.State.GetPendingMonstersAsync(sessionId));
        Assert.Equal(RedisType.List, await _db.KeyTypeAsync($"session:{sessionId}:pending_monsters"));

        await redis.State.ClearPendingMonstersAsync(sessionId);
        Assert.Empty(await redis.State.GetPendingMonstersAsync(sessionId));
    }

    [Fact]
    public async Task A_plain_name_left_by_an_old_version_reads_as_a_manual_monster()
    {
        var sessionId = RedisFixture.NewId();
        await _db.ListRightPushAsync($"session:{sessionId}:pending_monsters", "Old Goblin");

        Assert.Equal([new PendingMonster("Old Goblin", false)], await redis.State.GetPendingMonstersAsync(sessionId));
    }
}
