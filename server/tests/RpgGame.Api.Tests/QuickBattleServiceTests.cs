using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RpgGame.Api.Models;
using RpgGame.Api.QuickBattle;
using RpgGame.Api.Services;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;

namespace RpgGame.Api.Tests;

public class QuickBattleServiceTests
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Mock<IRedisGameState> _redis = new();
    private ActiveEncounterEntry? _active;
    private MapEntry? _map;
    private readonly Dictionary<string, (int Hp, string Profile)> _seeded = [];

    public QuickBattleServiceTests()
    {
        _redis.Setup(r => r.SetActiveEncounterAsync(It.IsAny<string>(), It.IsAny<ActiveEncounterEntry>()))
            .Callback<string, ActiveEncounterEntry>((_, e) => _active = e).Returns(Task.CompletedTask);
        _redis.Setup(r => r.SetMapAsync(It.IsAny<string>(), It.IsAny<MapEntry>()))
            .Callback<string, MapEntry>((_, m) => _map = m).Returns(Task.CompletedTask);
        _redis.Setup(r => r.SeedCharacterLiveStateAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .Callback<string, int, string>((key, hp, profile) => _seeded[key] = (hp, profile)).Returns(Task.CompletedTask);
    }

    private QuickBattleService Service() =>
        new(_db, _redis.Object, BattlePresetCatalog.FromAppDirectory(TestProfiles.Catalog), NullLogger<QuickBattleService>.Instance);

    [Fact]
    public void ListPresets_ReturnsShippedPresetsInMenuOrder()
    {
        var presets = Service().ListPresets();
        Assert.Equal("skirmish", presets[0].Id);
        Assert.Contains(presets, p => p.Id == "training-dummy");
    }

    [Fact]
    public async Task Start_CreatesCampaignWithCallerAsGm_SessionAndEncounter()
    {
        var r = await Service().StartAsync("user-1", "skirmish");

        var member = await _db.CampaignMembers.SingleAsync();
        Assert.Equal(("user-1", CampaignRole.Gm, r.Session.CampaignId), (member.UserId, member.Role, member.CampaignId));
        var session = await _db.GameSessions.SingleAsync();
        Assert.Equal((r.Session.GameSessionId, r.Session.JoinCode), (session.Id, session.JoinCode));
        Assert.Equal(6, session.JoinCode.Length);
        var encounter = await _db.Encounters.Include(e => e.Combatants).SingleAsync();
        Assert.Equal(session.Id, encounter.GameSessionId);
        Assert.Equal(6, encounter.Combatants.Count);
        Assert.Equal("skirmish", r.Session.PresetId);
    }

    [Fact]
    public async Task Start_PartyIsTheCallers_EnemiesAreAi_StatsComeFromThePreset()
    {
        await Service().StartAsync("user-1", "skirmish");

        var combatants = await _db.EncounterCombatants.ToListAsync();
        Assert.All(combatants, c => Assert.Equal("user-1", c.UserId));
        Assert.All(combatants, c => Assert.Null(c.CharacterId));

        var bruiser = combatants.Single(c => c.CharacterName == "Bruiser");
        Assert.Equal(("player", false), (bruiser.CombatantType, bruiser.IsAiControlled));
        Assert.Equal(("bruiser", 24, 4, 8, "d8"),
            (bruiser.Profile!.ProfileId, bruiser.Profile.MaxHp, bruiser.Profile.MovementRange, bruiser.Profile.Speed, bruiser.Profile.BaseDamage));
        var goblin = combatants.Single(c => c.CharacterName == "Goblin 1");
        Assert.Equal(("monster", true, "grunt", 10), (goblin.CombatantType, goblin.IsAiControlled, goblin.Profile!.ProfileId, goblin.Profile.MaxHp));
        // Each row owns its own copy: changing one goblin's values doesn't touch another's.
        Assert.NotSame(goblin.Profile, combatants.Single(c => c.CharacterName == "Goblin 2").Profile);
    }

    [Fact]
    public async Task Start_StoresTheWholeProfile_IncludingApDownsAndAbilities()
    {
        // Regression: the stored copy used to drop MaxAp and CanBeDowned (and would have dropped abilities).
        await Service().StartAsync("user-1", "abilities");

        var combatants = await _db.EncounterCombatants.ToListAsync();
        var skirmisher = combatants.Single(c => c.CharacterName == "Skirmisher").Profile!;
        Assert.Equal(5, skirmisher.MaxAp);
        var bruiser = combatants.Single(c => c.CharacterName == "Bruiser").Profile!;
        Assert.Equal(["power_strike", "cleave"], bruiser.Abilities!.Select(a => a.Id));
        Assert.Equal(["twin_shot", "firebolt"], combatants.Single(c => c.CharacterName == "Scout").Profile!.Abilities!.Select(a => a.Id));

        await Service().StartAsync("user-2", "rescue");
        var archer = (await _db.EncounterCombatants.ToListAsync()).Single(c => c.CharacterName == "Archer").Profile!;
        Assert.Equal(1, archer.Dodge);
    }

    [Fact]
    public async Task Start_SeedsMapHpAndAnActiveEncounterWithTheTimerOff()
    {
        var r = await Service().StartAsync("user-1", "skirmish");
        var bruiser = await _db.EncounterCombatants.SingleAsync(c => c.CharacterName == "Bruiser");
        var bruiserId = bruiser.Id.ToString();

        Assert.False(_active!.PlanningTimerEnabled);
        Assert.Equal(6, _active.Combatants.Count);
        Assert.Equal(new Position(8, 2), _map!.Positions[bruiserId]);
        Assert.Equal(24, _seeded[bruiserId].Hp); // keyed by combatant ID: there's no character

        Assert.Equal(0, r.Encounter.PlanningPayload.PlanningTimerDurationSeconds);
        var dto = r.Encounter.Payload.Combatants.Single(c => c.Id == bruiserId);
        Assert.Equal((24, 24), (dto.CurrentHp, dto.MaxHp));
        Assert.Equal(("bruiser", "Bruiser", 4), (dto.ProfileId, dto.ProfileName, dto.MovementRange));
        Assert.Equal("bruiser", _seeded[bruiserId].Profile); // character:live records the profile id
        Assert.True(r.Encounter.Payload.Combatants.Single(c => c.CharacterName == "Goblin 1").IsAiControlled);
    }

    [Fact]
    public async Task Start_UnknownPreset_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<GameActionException>(() => Service().StartAsync("user-1", "nope"));
        Assert.Contains("nope", ex.Message);
        Assert.Empty(_db.Campaigns);
    }
}
