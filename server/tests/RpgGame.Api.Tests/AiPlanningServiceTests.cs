using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Models;
using RpgGame.Api.Services;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;
using System.Text.Json;

namespace RpgGame.Api.Tests;

public class AiPlanningServiceTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly Guid _encounterId = Guid.NewGuid();
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Mock<IRedisGameState> _redis = new();
    private readonly Dictionary<string, string> _storedPlans = [];
    private readonly List<string> _lockedIn = [];
    private readonly Dictionary<string, Position> _positions = [];
    private readonly HashSet<string> _defeated = [];

    private readonly Guid _heroCharacterId = Guid.NewGuid();

    public AiPlanningServiceTests()
    {
        _redis.Setup(r => r.GetMapAsync(_encounterId.ToString()))
            .ReturnsAsync(() => new MapEntry(20, 20, _positions, []));
        _redis.Setup(r => r.GetCharacterHpAsync(It.IsAny<string>())).ReturnsAsync(20);
        _redis.Setup(r => r.IsDefeatedAsync(_encounterId.ToString(), It.IsAny<string>()))
            .ReturnsAsync((string _, string id) => _defeated.Contains(id));
        _redis.Setup(r => r.HasPlannedActionAsync(_encounterId.ToString(), It.IsAny<string>()))
            .ReturnsAsync((string _, string id) => _storedPlans.ContainsKey(id));
        _redis.Setup(r => r.SetPlannedActionAsync(_encounterId.ToString(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((_, id, json) => _storedPlans[id] = json)
            .Returns(Task.CompletedTask);
        _redis.Setup(r => r.AddLockedInBatchAsync(_encounterId.ToString(), It.IsAny<IEnumerable<string>>()))
            .Callback<string, IEnumerable<string>>((_, ids) => _lockedIn.AddRange(ids))
            .Returns(Task.CompletedTask);
    }

    private async Task<Guid> Add(string name, string type, int x, int y, bool ai = false, CombatProfileValues? profile = null)
    {
        var id = Guid.NewGuid();
        if (!_db.Encounters.Any(e => e.Id == _encounterId))
            _db.Encounters.Add(new Encounter { Id = _encounterId, GameSessionId = Guid.NewGuid(), StartedAt = DateTimeOffset.UtcNow });
        _db.EncounterCombatants.Add(new EncounterCombatant
        {
            Id = id,
            EncounterId = _encounterId,
            UserId = type == "player" ? "player-user" : "gm-user",
            CharacterId = type == "player" ? _heroCharacterId : null,
            CharacterName = name,
            CombatantType = type,
            IsAiControlled = ai,
            Profile = profile,
        });
        await _db.SaveChangesAsync();
        _positions[id.ToString()] = new Position(x, y);
        return id;
    }

    private AiPlanningService Service() => new(_db, _redis.Object, NullLogger<AiPlanningService>.Instance);

    private PlannedActionEntry StoredPlan(Guid id) => Assert.Single(StoredQueue(id));

    private IReadOnlyList<PlannedActionEntry> StoredQueue(Guid id) =>
        JsonSerializer.Deserialize<PlannedActionArray>(_storedPlans[id.ToString()], Json)!.Actions;

    [Fact]
    public async Task AdjacentAiMonster_PlansMelee_DistantOne_PlansMove_BothLockedIn()
    {
        var hero = await Add("Hero", "player", 5, 5);
        var adjacent = await Add("Goblin", "monster", 5, 6, ai: true);
        var distant = await Add("Orc", "monster", 5, 19, ai: true);

        var result = await Service().PlanAndLockInAsync(_encounterId);

        // Default profile: 4 AP, so two 2-AP melees (3B.2).
        var melee = new PlannedActionEntry("melee_attack", null, hero.ToString());
        Assert.Equal([melee, melee], StoredQueue(adjacent));
        var move = StoredPlan(distant);
        Assert.Equal("move", move.Type);
        Assert.Equal(new PositionDto(5, 13), move.TargetCell);

        Assert.Equivalent(new[] { adjacent.ToString(), distant.ToString() }, _lockedIn);
        Assert.Equivalent(new[] { "Goblin", "Orc" }, result.Select(r => r.CharacterName));
    }

    [Fact]
    public async Task AiAbilityPlan_IsStoredWithItsAbilityId()
    {
        // Regression (3B.3): the stored entry used to lose the ability id, so the resolver refused it.
        _redis.Setup(r => r.GetCooldownsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new Dictionary<string, int>());
        var hero = await Add("Hero", "player", 5, 5);
        var ogre = await Add("Ogre", "monster", 5, 6, ai: true, profile: TestProfiles.Catalog.Find("brute")!.Snapshot());

        await Service().PlanAndLockInAsync(_encounterId);

        var entry = StoredPlan(ogre);
        Assert.Equal(("ability", "power_strike", hero.ToString()), (entry.Type, entry.AbilityId, entry.TargetCombatantId));
    }

    [Fact]
    public async Task ManualAndDefeatedMonsters_AreLeftAlone()
    {
        await Add("Hero", "player", 5, 5);
        var manual = await Add("Manual", "monster", 5, 6);
        var dead = await Add("Dead", "monster", 4, 6, ai: true);
        _defeated.Add(dead.ToString());

        var result = await Service().PlanAndLockInAsync(_encounterId);

        Assert.Empty(result);
        Assert.Empty(_storedPlans);
        _redis.Verify(r => r.AddLockedInBatchAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never);
        Assert.False(_storedPlans.ContainsKey(manual.ToString()));
    }

    [Fact]
    public async Task ExistingPlan_IsNotOverwritten()
    {
        await Add("Hero", "player", 5, 5);
        var goblin = await Add("Goblin", "monster", 5, 6, ai: true);
        var gmOverride = """{"actions":[{"type":"defend"}]}""";
        _storedPlans[goblin.ToString()] = gmOverride;

        var result = await Service().PlanAndLockInAsync(_encounterId);

        Assert.Empty(result);
        Assert.Equal(gmOverride, _storedPlans[goblin.ToString()]);
    }

    [Fact]
    public async Task OnlyCombatantId_PlansJustThatMonster()
    {
        await Add("Hero", "player", 5, 5);
        var first = await Add("Goblin", "monster", 5, 6, ai: true);
        var added = await Add("Late Goblin", "monster", 4, 6, ai: true);

        var result = await Service().PlanAndLockInAsync(_encounterId, added.ToString());

        Assert.Equal("Late Goblin", Assert.Single(result).CharacterName);
        Assert.False(_storedPlans.ContainsKey(first.ToString()));
        Assert.True(_storedPlans.ContainsKey(added.ToString()));
    }
}
