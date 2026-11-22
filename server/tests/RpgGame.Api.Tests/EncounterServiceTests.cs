using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RpgGame.Api.Models;
using RpgGame.Api.Services;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;

namespace RpgGame.Api.Tests;

public class EncounterServiceTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<(Guid sessionId, Guid campaignId)> SetupGmSession(
        AppDbContext db, string joinCode, string gmUserId)
    {
        var campaignId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        db.GameSessions.Add(new GameSession
        {
            Id = sessionId,
            CampaignId = campaignId,
            JoinCode = joinCode,
            StartedAt = DateTimeOffset.UtcNow,
        });
        db.CampaignMembers.Add(new CampaignMember
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            UserId = gmUserId,
            Role = CampaignRole.Gm,
        });
        await db.SaveChangesAsync();
        return (sessionId, campaignId);
    }

    [Fact]
    public async Task StartEncounter_RejectsNonGmUser()
    {
        await using var db = CreateDb();
        var campaignId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        const string userId = "player-user";
        const string joinCode = "ABC123";

        db.GameSessions.Add(new GameSession
        {
            Id = sessionId,
            CampaignId = campaignId,
            JoinCode = joinCode,
            StartedAt = DateTimeOffset.UtcNow,
        });
        db.CampaignMembers.Add(new CampaignMember
        {
            Id = Guid.NewGuid(),
            CampaignId = campaignId,
            UserId = userId,
            Role = CampaignRole.Player,
        });
        await db.SaveChangesAsync();

        var mockRedis = new Mock<IRedisGameState>();
        var service = new EncounterService(db, mockRedis.Object, TestProfiles.Catalog, NullLogger<EncounterService>.Instance);

        var ex = await Assert.ThrowsAsync<GameActionException>(
            () => service.StartEncounterAsync(joinCode, userId));

        Assert.Contains("Only the GM", ex.Message);
    }

    [Fact]
    public async Task AddMonster_RejectsNonGmUser()
    {
        await using var db = CreateDb();
        var campaignId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        const string joinCode = "MON01";

        db.GameSessions.Add(new GameSession { Id = sessionId, CampaignId = campaignId, JoinCode = joinCode, StartedAt = DateTimeOffset.UtcNow });
        db.CampaignMembers.Add(new CampaignMember { Id = Guid.NewGuid(), CampaignId = campaignId, UserId = "player-1", Role = CampaignRole.Player });
        await db.SaveChangesAsync();

        var mockRedis = new Mock<IRedisGameState>();
        var service = new EncounterService(db, mockRedis.Object, TestProfiles.Catalog, NullLogger<EncounterService>.Instance);

        var ex = await Assert.ThrowsAsync<GameActionException>(
            () => service.AddMonsterAsync(joinCode, "player-1", "Goblin", isAiControlled: false));

        Assert.True(ex.IsActionRejected);
        Assert.Contains("Only the GM", ex.Message);
    }

    [Fact]
    public async Task AddMonster_RejectsWhenNotInPlanningPhase()
    {
        await using var db = CreateDb();
        const string gmUserId = "gm-1";
        const string joinCode = "MON02";
        var (sessionId, _) = await SetupGmSession(db, joinCode, gmUserId);

        var encounterId = Guid.NewGuid().ToString();
        var resolvingEntry = new ActiveEncounterEntry(encounterId, "resolving", 1,
            DateTimeOffset.UtcNow.AddSeconds(30), new List<CombatantRef>());

        var mockRedis = new Mock<IRedisGameState>();
        mockRedis.Setup(r => r.GetActiveEncounterAsync(sessionId.ToString())).ReturnsAsync(resolvingEntry);

        var service = new EncounterService(db, mockRedis.Object, TestProfiles.Catalog, NullLogger<EncounterService>.Instance);

        var ex = await Assert.ThrowsAsync<GameActionException>(
            () => service.AddMonsterAsync(joinCode, gmUserId, "Goblin", isAiControlled: false));

        Assert.True(ex.IsActionRejected);
        Assert.Contains("planning phase", ex.Message);
    }

    [Fact]
    public async Task AddMonster_SpawnsAtBottomRowAndUpdatesRedis()
    {
        await using var db = CreateDb();
        const string gmUserId = "gm-1";
        const string joinCode = "MON03";
        var (sessionId, _) = await SetupGmSession(db, joinCode, gmUserId);

        var encounterId = Guid.NewGuid();
        var planningEntry = new ActiveEncounterEntry(encounterId.ToString(), "planning", 1,
            DateTimeOffset.UtcNow.AddSeconds(30), new List<CombatantRef>());

        // Positions at y=19, x=0 and x=1 — monster should spawn at x=2
        var existingCombatantId = Guid.NewGuid().ToString();
        var existingCombatantId2 = Guid.NewGuid().ToString();
        var mapEntry = new MapEntry(20, 20, new Dictionary<string, Position>
        {
            [existingCombatantId] = new Position(0, 19),
            [existingCombatantId2] = new Position(1, 19),
        }, new List<string>());

        var mockRedis = new Mock<IRedisGameState>();
        mockRedis.Setup(r => r.GetActiveEncounterAsync(sessionId.ToString())).ReturnsAsync(planningEntry);
        mockRedis.Setup(r => r.GetMapAsync(encounterId.ToString())).ReturnsAsync(mapEntry);
        mockRedis.Setup(r => r.SetMapAsync(It.IsAny<string>(), It.IsAny<MapEntry>())).Returns(Task.CompletedTask);
        mockRedis.Setup(r => r.SeedCharacterLiveStateAsync(It.IsAny<string>(), 10, "grunt")).Returns(Task.CompletedTask);
        mockRedis.Setup(r => r.SetActiveEncounterAsync(It.IsAny<string>(), It.IsAny<ActiveEncounterEntry>())).Returns(Task.CompletedTask);

        var service = new EncounterService(db, mockRedis.Object, TestProfiles.Catalog, NullLogger<EncounterService>.Instance);

        // Two combatants already in the fight: the new monster goes after them.
        db.EncounterCombatants.AddRange(
            new EncounterCombatant { Id = Guid.Parse(existingCombatantId), EncounterId = encounterId, UserId = gmUserId, SortOrder = 0 },
            new EncounterCombatant { Id = Guid.Parse(existingCombatantId2), EncounterId = encounterId, UserId = gmUserId, SortOrder = 1 });
        await db.SaveChangesAsync();

        var result = await service.AddMonsterAsync(joinCode, gmUserId, "Goblin", isAiControlled: false);

        Assert.Equal(2, (await db.EncounterCombatants.SingleAsync(c => c.Id == Guid.Parse(result.MonsterDto.Id))).SortOrder);
        Assert.Equal(2, result.SpawnPosition.X);
        Assert.Equal(19, result.SpawnPosition.Y);
        Assert.Equal("monster", result.MonsterDto.CombatantType);
        // No profile given: the default enemy profile (grunt, 10 HP).
        Assert.Equal(("grunt", 10, 10), (result.MonsterDto.ProfileId, result.MonsterDto.CurrentHp, result.MonsterDto.MaxHp));
        mockRedis.Verify(r => r.SeedCharacterLiveStateAsync(It.IsAny<string>(), 10, "grunt"), Times.Once());
        mockRedis.Verify(r => r.SetActiveEncounterAsync(It.IsAny<string>(), It.Is<ActiveEncounterEntry>(e =>
            e.Combatants.Any(c => c.CombatantType == "monster"))), Times.Once());
    }

    [Fact]
    public async Task AddMonster_DefaultsBlankNameToMonster()
    {
        await using var db = CreateDb();
        const string gmUserId = "gm-1";
        const string joinCode = "MON04";
        var (sessionId, _) = await SetupGmSession(db, joinCode, gmUserId);

        var encounterId = Guid.NewGuid();
        var planningEntry = new ActiveEncounterEntry(encounterId.ToString(), "planning", 1,
            DateTimeOffset.UtcNow.AddSeconds(30), new List<CombatantRef>());
        var mapEntry = new MapEntry(20, 20, new Dictionary<string, Position>(), new List<string>());

        var mockRedis = new Mock<IRedisGameState>();
        mockRedis.Setup(r => r.GetActiveEncounterAsync(sessionId.ToString())).ReturnsAsync(planningEntry);
        mockRedis.Setup(r => r.GetMapAsync(encounterId.ToString())).ReturnsAsync(mapEntry);
        mockRedis.Setup(r => r.SetMapAsync(It.IsAny<string>(), It.IsAny<MapEntry>())).Returns(Task.CompletedTask);
        mockRedis.Setup(r => r.SeedCharacterLiveStateAsync(It.IsAny<string>(), 10, "grunt")).Returns(Task.CompletedTask);
        mockRedis.Setup(r => r.SetActiveEncounterAsync(It.IsAny<string>(), It.IsAny<ActiveEncounterEntry>())).Returns(Task.CompletedTask);

        var service = new EncounterService(db, mockRedis.Object, TestProfiles.Catalog, NullLogger<EncounterService>.Instance);

        var result = await service.AddMonsterAsync(joinCode, gmUserId, "   ", isAiControlled: false);

        Assert.Equal("Monster", result.MonsterDto.CharacterName);
    }

    [Fact]
    public async Task EndEncounter_MarksEndedLogsEventAndClearsLiveState()
    {
        await using var db = CreateDb();
        var (sessionId, _) = await SetupGmSession(db, "END123", "gm-user");
        var encounterId = Guid.NewGuid();
        db.Encounters.Add(new Encounter
        {
            Id = encounterId,
            GameSessionId = sessionId,
            Status = EncounterStatus.Active,
            RoundNumber = 1,
            StartedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var mockRedis = new Mock<IRedisGameState>();
        var service = new EncounterService(db, mockRedis.Object, TestProfiles.Catalog, NullLogger<EncounterService>.Instance);

        await service.EndEncounterAsync(encounterId, "party", 4);

        var encounter = await db.Encounters.SingleAsync(e => e.Id == encounterId);
        Assert.Equal(EncounterStatus.Ended, encounter.Status);
        Assert.NotNull(encounter.EndedAt);
        Assert.Equal(4, encounter.RoundNumber);

        var evt = await db.GameEvents.SingleAsync(e => e.EncounterId == encounterId);
        Assert.Equal("encounter_ended", evt.EventType);
        Assert.Contains("\"winningSide\":\"party\"", evt.Payload);
        Assert.Contains("\"roundNumber\":4", evt.Payload);

        mockRedis.Verify(r => r.ClearPlannedActionsAsync(encounterId.ToString()), Times.Once);
        mockRedis.Verify(r => r.ClearLockedInAsync(encounterId.ToString()), Times.Once);
        mockRedis.Verify(r => r.DeleteActiveEncounterAsync(sessionId.ToString()), Times.Once);
    }

    [Fact]
    public async Task StartEncounter_CarriesAiFlagFromPendingMonsterToCombatantRedisAndDtos()
    {
        await using var db = CreateDb();
        const string gmUserId = "gm-ai";
        var (sessionId, _) = await SetupGmSession(db, "AIFLAG", gmUserId);
        var character = new Character { Id = Guid.NewGuid(), UserId = "player-1", Name = "Hero", MaxHp = 12, CurrentHp = 12, CombatProfile = "bruiser" };
        db.Characters.Add(character);
        await db.SaveChangesAsync();

        var mockRedis = new Mock<IRedisGameState>();
        mockRedis.Setup(r => r.GetPlayersAsync(sessionId.ToString())).ReturnsAsync(new Dictionary<string, PlayerEntry>
        {
            ["player-1"] = new PlayerEntry("Hero", character.Id.ToString()),
        });
        mockRedis.Setup(r => r.GetPendingMonstersAsync(sessionId.ToString())).ReturnsAsync(
        [
            new PendingMonster("Goblin", IsAiControlled: true),
            new PendingMonster("Puppet", IsAiControlled: false, Profile: "brute"),
        ]);
        ActiveEncounterEntry? stored = null;
        mockRedis.Setup(r => r.SetActiveEncounterAsync(sessionId.ToString(), It.IsAny<ActiveEncounterEntry>()))
            .Callback<string, ActiveEncounterEntry>((_, e) => stored = e)
            .Returns(Task.CompletedTask);

        var service = new EncounterService(db, mockRedis.Object, TestProfiles.Catalog, NullLogger<EncounterService>.Instance);
        var result = await service.StartEncounterAsync("AIFLAG", gmUserId);

        var goblin = await db.EncounterCombatants.SingleAsync(c => c.CharacterName == "Goblin");
        var puppet = await db.EncounterCombatants.SingleAsync(c => c.CharacterName == "Puppet");
        Assert.True(goblin.IsAiControlled);
        Assert.False(puppet.IsAiControlled);

        Assert.True(stored!.Combatants.Single(c => c.Id == goblin.Id.ToString()).IsAiControlled);
        Assert.False(stored.Combatants.Single(c => c.Id == puppet.Id.ToString()).IsAiControlled);

        // Created in order: players, then the staged monsters.
        Assert.Equal(["Hero", "Goblin", "Puppet"],
            await db.EncounterCombatants.OrderBy(c => c.SortOrder).Select(c => c.CharacterName).ToListAsync());
        Assert.Equal([0, 1, 2], await db.EncounterCombatants.OrderBy(c => c.SortOrder).Select(c => c.SortOrder).ToListAsync());

        Assert.True(result.Payload.Combatants.Single(c => c.CharacterName == "Goblin").IsAiControlled);
        Assert.True(result.PlanningPayload.Combatants.Single(c => c.CharacterName == "Goblin").IsAiControlled);
        Assert.False(result.PlanningPayload.Combatants.Single(c => c.CharacterName == "Hero").IsAiControlled);
    }

    [Fact]
    public async Task StartEncounter_SnapshotsProfiles_AndEveryoneStartsAtProfileMaxHp()
    {
        await using var db = CreateDb();
        const string gmUserId = "gm-prof";
        var (sessionId, _) = await SetupGmSession(db, "PROF01", gmUserId);
        // The character's own (D&D-era) HP fields are ignored: the bruiser profile has 24 HP.
        var hero = new Character { Id = Guid.NewGuid(), UserId = "p1", Name = "Hero", MaxHp = 99, CurrentHp = 3, CombatProfile = "bruiser" };
        var lost = new Character { Id = Guid.NewGuid(), UserId = "p2", Name = "Lost", CombatProfile = "no-such-profile" };
        db.Characters.AddRange(hero, lost);
        await db.SaveChangesAsync();

        var seeded = new Dictionary<string, (int Hp, string Profile)>();
        var mockRedis = new Mock<IRedisGameState>();
        mockRedis.Setup(r => r.GetPlayersAsync(sessionId.ToString())).ReturnsAsync(new Dictionary<string, PlayerEntry>
        {
            ["p1"] = new PlayerEntry("Hero", hero.Id.ToString()),
            ["p2"] = new PlayerEntry("Lost", lost.Id.ToString()),
        });
        mockRedis.Setup(r => r.GetPendingMonstersAsync(sessionId.ToString())).ReturnsAsync(
        [
            new PendingMonster("Ogre", IsAiControlled: true, Profile: "brute"),
            new PendingMonster("Old Goblin", IsAiControlled: false), // staged before 3B.1: default enemy profile
        ]);
        mockRedis.Setup(r => r.SeedCharacterLiveStateAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .Callback<string, int, string>((key, hp, profile) => seeded[key] = (hp, profile))
            .Returns(Task.CompletedTask);

        var service = new EncounterService(db, mockRedis.Object, TestProfiles.Catalog, NullLogger<EncounterService>.Instance);
        var result = await service.StartEncounterAsync("PROF01", gmUserId);

        var rows = await db.EncounterCombatants.ToDictionaryAsync(c => c.CharacterName);
        Assert.Equal(("bruiser", 24, 4), (rows["Hero"].Profile!.ProfileId, rows["Hero"].Profile!.MaxHp, rows["Hero"].Profile!.MovementRange));
        Assert.Equal("skirmisher", rows["Lost"].Profile!.ProfileId); // unknown id → default party profile
        Assert.Equal("brute", rows["Ogre"].Profile!.ProfileId);
        Assert.Equal("grunt", rows["Old Goblin"].Profile!.ProfileId);

        Assert.Equal((24, "bruiser"), seeded[hero.Id.ToString()]); // players keyed by character ID
        Assert.Equal((28, "brute"), seeded[rows["Ogre"].Id.ToString()]);

        var heroDto = result.Payload.Combatants.Single(c => c.CharacterName == "Hero");
        Assert.Equal((24, 24, "Bruiser", 4), (heroDto.CurrentHp, heroDto.MaxHp, heroDto.ProfileName, heroDto.MovementRange));
        var ogreDto = result.PlanningPayload.Combatants.Single(c => c.CharacterName == "Ogre");
        Assert.Equal(("brute", 28, 4), (ogreDto.ProfileId, ogreDto.MaxHp, ogreDto.MovementRange));
    }

    [Fact]
    public async Task AddMonster_WithProfile_SpawnsWithThatProfile()
    {
        await using var db = CreateDb();
        var (sessionId, _) = await SetupGmSession(db, "MON05", "gm-1");
        var encounterId = Guid.NewGuid();
        var mockRedis = new Mock<IRedisGameState>();
        mockRedis.Setup(r => r.GetActiveEncounterAsync(sessionId.ToString())).ReturnsAsync(
            new ActiveEncounterEntry(encounterId.ToString(), "planning", 1, DateTimeOffset.UtcNow, []));
        mockRedis.Setup(r => r.GetMapAsync(encounterId.ToString())).ReturnsAsync(new MapEntry(20, 20, [], []));

        var service = new EncounterService(db, mockRedis.Object, TestProfiles.Catalog, NullLogger<EncounterService>.Instance);
        var result = await service.AddMonsterAsync("MON05", "gm-1", "Ogre", isAiControlled: true, profileId: "brute");

        var row = await db.EncounterCombatants.SingleAsync();
        Assert.Equal(("brute", 28, "d10"), (row.Profile!.ProfileId, row.Profile.MaxHp, row.Profile.BaseDamage));
        Assert.Equal((28, 28, "Brute", 4), (result.MonsterDto.CurrentHp, result.MonsterDto.MaxHp, result.MonsterDto.ProfileName, result.MonsterDto.MovementRange));
        mockRedis.Verify(r => r.SeedCharacterLiveStateAsync(row.Id.ToString(), 28, "brute"), Times.Once());
    }

    [Fact]
    public async Task AddMonster_UnknownProfile_IsRejected()
    {
        await using var db = CreateDb();
        await SetupGmSession(db, "MON06", "gm-1");
        var service = new EncounterService(db, new Mock<IRedisGameState>().Object, TestProfiles.Catalog, NullLogger<EncounterService>.Instance);

        var ex = await Assert.ThrowsAsync<GameActionException>(
            () => service.AddMonsterAsync("MON06", "gm-1", "Dragon", isAiControlled: false, profileId: "dragon"));

        Assert.True(ex.IsActionRejected);
        Assert.Contains("dragon", ex.Message);
    }

    [Fact]
    public async Task AddMonster_BeforeTheEncounter_StagesTheProfile()
    {
        await using var db = CreateDb();
        var (sessionId, _) = await SetupGmSession(db, "MON07", "gm-1");
        var mockRedis = new Mock<IRedisGameState>();

        var service = new EncounterService(db, mockRedis.Object, TestProfiles.Catalog, NullLogger<EncounterService>.Instance);
        var result = await service.AddMonsterAsync("MON07", "gm-1", "Ogre", isAiControlled: false, profileId: "brute");

        mockRedis.Verify(r => r.AddPendingMonsterAsync(sessionId.ToString(), new PendingMonster("Ogre", false, "brute")), Times.Once());
        Assert.Equal((28, "Brute"), (result.MonsterDto.MaxHp, result.MonsterDto.ProfileName));
    }

    [Theory]
    [InlineData("{\"name\":\"Goblin\",\"isAiControlled\":true,\"profile\":\"brute\"}", "Goblin", true, "brute")]
    [InlineData("{\"name\":\"Goblin\",\"isAiControlled\":true}", "Goblin", true, null)] // before 3B.1 profiles
    [InlineData("Old Goblin", "Old Goblin", false, null)]       // entries written before the AI flag existed
    [InlineData("{not json", "{not json", false, null)]
    public void PendingMonster_ParsesJsonAndPlainNames(string stored, string name, bool ai, string? profile) =>
        Assert.Equal(new PendingMonster(name, ai, profile), RedisGameState.ParsePendingMonster(stored));
}
