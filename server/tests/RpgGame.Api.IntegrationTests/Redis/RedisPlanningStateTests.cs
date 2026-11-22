using StackExchange.Redis;

namespace RpgGame.Api.IntegrationTests.Redis;

/// <summary>Planning phase state: planned action queues and lock-ins.</summary>
[Collection(RedisCollection.Name)]
public class RedisPlanningStateTests(RedisFixture redis)
{
    private readonly IDatabase _db = redis.Db;

    private const string MoveJson = """{"actions":[{"type":"move","targetCell":{"x":1,"y":2}}]}""";
    private const string DefendJson = """{"actions":[{"type":"defend"}]}""";

    [Fact]
    public async Task Planned_actions_round_trip_per_combatant_and_replace()
    {
        var encounterId = RedisFixture.NewId();

        await redis.State.SetPlannedActionAsync(encounterId, "c1", DefendJson);
        await redis.State.SetPlannedActionAsync(encounterId, "c2", DefendJson);
        await redis.State.SetPlannedActionAsync(encounterId, "c1", MoveJson);

        var all = await redis.State.GetAllPlannedActionsAsync(encounterId);
        Assert.Equal(2, all.Count);
        Assert.Equal(MoveJson, all["c1"]);
        Assert.Equal(DefendJson, all["c2"]);
        Assert.True(await redis.State.HasPlannedActionAsync(encounterId, "c1"));
        Assert.False(await redis.State.HasPlannedActionAsync(encounterId, "c3"));
        Assert.Equal(RedisType.Hash, await _db.KeyTypeAsync($"encounter:{encounterId}:planned_actions"));
    }

    [Fact]
    public async Task Clearing_one_plan_leaves_the_others()
    {
        var encounterId = RedisFixture.NewId();
        await redis.State.SetPlannedActionAsync(encounterId, "c1", DefendJson);
        await redis.State.SetPlannedActionAsync(encounterId, "c2", DefendJson);

        await redis.State.ClearPlannedActionAsync(encounterId, "c1");

        Assert.False(await redis.State.HasPlannedActionAsync(encounterId, "c1"));
        Assert.True(await redis.State.HasPlannedActionAsync(encounterId, "c2"));
    }

    [Fact]
    public async Task Clearing_all_plans_removes_the_key()
    {
        var encounterId = RedisFixture.NewId();
        await redis.State.SetPlannedActionAsync(encounterId, "c1", DefendJson);

        await redis.State.ClearPlannedActionsAsync(encounterId);

        Assert.Empty(await redis.State.GetAllPlannedActionsAsync(encounterId));
        Assert.False(await _db.KeyExistsAsync($"encounter:{encounterId}:planned_actions"));
    }

    [Fact]
    public async Task Lock_ins_are_a_set_counted_once_per_combatant()
    {
        var encounterId = RedisFixture.NewId();

        await redis.State.AddLockedInAsync(encounterId, "c1");
        await redis.State.AddLockedInBatchAsync(encounterId, ["c1", "c2", "c3"]);
        await redis.State.AddLockedInBatchAsync(encounterId, []); // no-op, must not throw

        Assert.Equal(3, await redis.State.GetLockedInCountAsync(encounterId));
        Assert.True(await redis.State.IsLockedInAsync(encounterId, "c2"));
        Assert.False(await redis.State.IsLockedInAsync(encounterId, "c4"));
        Assert.Equal(RedisType.Set, await _db.KeyTypeAsync($"encounter:{encounterId}:locked_in"));

        await redis.State.ClearLockedInAsync(encounterId);
        Assert.Equal(0, await redis.State.GetLockedInCountAsync(encounterId));
        Assert.False(await redis.State.IsLockedInAsync(encounterId, "c1"));
    }
}
