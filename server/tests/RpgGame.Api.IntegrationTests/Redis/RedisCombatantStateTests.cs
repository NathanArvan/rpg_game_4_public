using RpgGame.Api.Models;
using StackExchange.Redis;

namespace RpgGame.Api.IntegrationTests.Redis;

/// <summary>Per-combatant state: live HP, defeated / status / revive (3B.5, 3B.3) and cooldowns (3B.3).</summary>
[Collection(RedisCollection.Name)]
public class RedisCombatantStateTests(RedisFixture redis)
{
    private readonly IDatabase _db = redis.Db;

    // --- Live HP ---

    [Fact]
    public async Task Seeded_live_state_holds_hp_and_profile_in_the_character_live_hash()
    {
        var id = RedisFixture.NewId();

        await redis.State.SeedCharacterLiveStateAsync(id, 16, "skirmisher");

        Assert.Equal(16, await redis.State.GetCharacterHpAsync(id));
        Assert.Equal(RedisType.Hash, await _db.KeyTypeAsync($"character:live:{id}"));
        Assert.Equal("16", (string?)await _db.HashGetAsync($"character:live:{id}", "currentHp"));
        Assert.Equal("skirmisher", (string?)await _db.HashGetAsync($"character:live:{id}", "profile"));
    }

    [Fact]
    public async Task Hp_can_be_decremented_and_set()
    {
        var id = RedisFixture.NewId();
        await redis.State.SeedCharacterLiveStateAsync(id, 16, "skirmisher");

        Assert.Equal(11, await redis.State.DecrementHpAsync(id, 5));
        Assert.Equal(11, await redis.State.GetCharacterHpAsync(id));

        await redis.State.SetHpAsync(id, 3);
        Assert.Equal(3, await redis.State.GetCharacterHpAsync(id));
        Assert.Equal("skirmisher", (string?)await _db.HashGetAsync($"character:live:{id}", "profile"));
    }

    [Fact]
    public async Task Hp_of_an_unknown_combatant_reads_as_zero()
    {
        Assert.Equal(0, await redis.State.GetCharacterHpAsync(RedisFixture.NewId()));
    }

    // --- Defeated, status, revive ---

    [Fact]
    public async Task Defeated_is_a_set_per_encounter()
    {
        var encounterId = RedisFixture.NewId();

        Assert.False(await redis.State.IsDefeatedAsync(encounterId, "c1"));
        await redis.State.MarkDefeatedAsync(encounterId, "c1");

        Assert.True(await redis.State.IsDefeatedAsync(encounterId, "c1"));
        Assert.False(await redis.State.IsDefeatedAsync(encounterId, "c2"));
        Assert.False(await redis.State.IsDefeatedAsync(RedisFixture.NewId(), "c1"));
        Assert.Equal(RedisType.Set, await _db.KeyTypeAsync($"encounter:{encounterId}:defeated"));
    }

    [Theory]
    [InlineData("downed", 2, "downed:2")]
    [InlineData("stable", 0, "stable:0")]
    [InlineData("dead", 0, "dead:0")]
    public async Task Status_round_trips_as_status_colon_rounds(string status, int rounds, string stored)
    {
        var encounterId = RedisFixture.NewId();

        await redis.State.SetStatusAsync(encounterId, "c1", new CombatantStatusEntry(status, rounds));

        Assert.Equal(new CombatantStatusEntry(status, rounds), await redis.State.GetStatusAsync(encounterId, "c1"));
        Assert.Equal(RedisType.Hash, await _db.KeyTypeAsync($"encounter:{encounterId}:status"));
        Assert.Equal(stored, (string?)await _db.HashGetAsync($"encounter:{encounterId}:status", "c1"));
    }

    [Fact]
    public async Task No_status_entry_reads_as_null()
    {
        Assert.Null(await redis.State.GetStatusAsync(RedisFixture.NewId(), "c1"));
    }

    [Fact]
    public async Task Revive_clears_both_the_defeated_set_and_the_status_entry()
    {
        var encounterId = RedisFixture.NewId();
        await redis.State.MarkDefeatedAsync(encounterId, "c1");
        await redis.State.SetStatusAsync(encounterId, "c1", new CombatantStatusEntry("downed", 1));
        await redis.State.MarkDefeatedAsync(encounterId, "c2");
        await redis.State.SetStatusAsync(encounterId, "c2", new CombatantStatusEntry("stable", 0));

        await redis.State.ReviveAsync(encounterId, "c1");

        Assert.False(await redis.State.IsDefeatedAsync(encounterId, "c1"));
        Assert.Null(await redis.State.GetStatusAsync(encounterId, "c1"));
        // Only the revived combatant is touched.
        Assert.True(await redis.State.IsDefeatedAsync(encounterId, "c2"));
        Assert.Equal(new CombatantStatusEntry("stable", 0), await redis.State.GetStatusAsync(encounterId, "c2"));
    }

    // --- Cooldowns ---

    [Fact]
    public async Task Cooldowns_round_trip_as_json_per_combatant()
    {
        var encounterId = RedisFixture.NewId();

        await redis.State.SetCooldownsAsync(encounterId, "c1", new Dictionary<string, int> { ["power_strike"] = 2, ["mend"] = 1 });

        Assert.Equal(new Dictionary<string, int> { ["power_strike"] = 2, ["mend"] = 1 },
            await redis.State.GetCooldownsAsync(encounterId, "c1"));
        Assert.Empty(await redis.State.GetCooldownsAsync(encounterId, "c2"));
        Assert.Equal(RedisType.Hash, await _db.KeyTypeAsync($"encounter:{encounterId}:cooldowns"));
        // Ability ids are kept as written (not camel-cased) — they're looked up by id.
        Assert.Equal("""{"power_strike":2,"mend":1}""", (string?)await _db.HashGetAsync($"encounter:{encounterId}:cooldowns", "c1"));
    }

    [Fact]
    public async Task Setting_no_cooldowns_removes_the_entry()
    {
        var encounterId = RedisFixture.NewId();
        await redis.State.SetCooldownsAsync(encounterId, "c1", new Dictionary<string, int> { ["cleave"] = 1 });

        await redis.State.SetCooldownsAsync(encounterId, "c1", new Dictionary<string, int>());

        Assert.Empty(await redis.State.GetCooldownsAsync(encounterId, "c1"));
        Assert.False(await _db.HashExistsAsync($"encounter:{encounterId}:cooldowns", "c1"));
    }
}
