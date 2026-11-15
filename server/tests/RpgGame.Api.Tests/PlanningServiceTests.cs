using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Models;
using RpgGame.Api.Services;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;

namespace RpgGame.Api.Tests;

public class PlanningServiceTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static ActiveEncounterEntry MakeActiveEncounter(string encounterId, string userId, string combatantId) =>
        new(encounterId, "planning", 1, DateTimeOffset.UtcNow.AddSeconds(30),
            new List<CombatantRef> { new(combatantId, userId) });

    private static ActiveEncounterEntry MakeActiveEncounterWithMonster(
        string encounterId, string gmUserId, string monsterId, string? playerId = null, string? playerCombatantId = null)
    {
        var combatants = new List<CombatantRef> { new(monsterId, gmUserId, "monster") };
        if (playerId is not null && playerCombatantId is not null)
            combatants.Add(new CombatantRef(playerCombatantId, playerId, "player"));
        return new(encounterId, "planning", 1, DateTimeOffset.UtcNow.AddSeconds(30), combatants);
    }

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
    public async Task PlanAction_RejectsMoveOutOfRange()
    {
        await using var db = CreateDb();
        var sessionId = Guid.NewGuid();
        var encounterId = Guid.NewGuid();
        const string userId = "user-1";
        const string joinCode = "PLAN01";
        var combatantId = Guid.NewGuid().ToString();

        db.GameSessions.Add(new GameSession
        {
            Id = sessionId,
            CampaignId = Guid.NewGuid(),
            JoinCode = joinCode,
            StartedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var activeEntry = MakeActiveEncounter(encounterId.ToString(), userId, combatantId);
        var mapEntry = new MapEntry(
            20, 20,
            new Dictionary<string, Position> { [combatantId] = new Position(0, 0) },
            new List<string>());

        var mockRedis = new Mock<IRedisGameState>();
        mockRedis.Setup(r => r.GetActiveEncounterAsync(sessionId.ToString())).ReturnsAsync(activeEntry);
        mockRedis.Setup(r => r.IsDefeatedAsync(encounterId.ToString(), combatantId)).ReturnsAsync(false);
        mockRedis.Setup(r => r.GetMapAsync(encounterId.ToString())).ReturnsAsync(mapEntry);

        var service = new PlanningService(db, mockRedis.Object, NullLogger<PlanningService>.Instance);

        var action = new PlannedActionArray(new List<PlannedActionEntry>
        {
            new("move", new PositionDto(0, 7), null),
        });

        var ex = await Assert.ThrowsAsync<GameActionException>(
            () => service.PlanActionAsync(joinCode, userId, action));

        Assert.True(ex.IsActionRejected);
        Assert.Contains("maximum range", ex.Message);
    }

    /// <summary>One player combatant whose stored profile has the given movement range, standing at (0, 0).</summary>
    private static async Task<(PlanningService Service, Mock<IRedisGameState> Redis, string JoinCode, string CombatantId)> SetupMover(
        AppDbContext db, int movementRange)
    {
        var sessionId = Guid.NewGuid();
        var encounterId = Guid.NewGuid();
        var combatantId = Guid.NewGuid();
        const string joinCode = "MOVE01";
        db.GameSessions.Add(new GameSession { Id = sessionId, CampaignId = Guid.NewGuid(), JoinCode = joinCode, StartedAt = DateTimeOffset.UtcNow });
        db.EncounterCombatants.Add(new EncounterCombatant
        {
            Id = combatantId, EncounterId = encounterId, UserId = "user-1", CharacterName = "Mover",
            CombatantType = "player", Profile = TestProfiles.Values(move: movementRange),
        });
        await db.SaveChangesAsync();

        var id = combatantId.ToString();
        var redis = new Mock<IRedisGameState>();
        redis.Setup(r => r.GetActiveEncounterAsync(sessionId.ToString())).ReturnsAsync(MakeActiveEncounter(encounterId.ToString(), "user-1", id));
        redis.Setup(r => r.GetMapAsync(encounterId.ToString())).ReturnsAsync(
            new MapEntry(20, 20, new Dictionary<string, Position> { [id] = new Position(0, 0) }, []));
        return (new PlanningService(db, redis.Object, NullLogger<PlanningService>.Instance), redis, joinCode, id);
    }

    private static PlannedActionArray MoveTo(int x, int y) => new([new("move", new PositionDto(x, y), null)]);

    [Theory]
    [InlineData(4, 4, true)]
    [InlineData(4, 5, false)]
    [InlineData(8, 8, true)]
    [InlineData(8, 9, false)]
    public async Task PlanAction_MoveRangeComesFromTheCombatantsProfile(int range, int distance, bool accepted)
    {
        await using var db = CreateDb();
        var (service, _, joinCode, _) = await SetupMover(db, range);

        if (accepted)
        {
            Assert.True((await service.PlanActionAsync(joinCode, "user-1", MoveTo(distance, 0))).Success);
        }
        else
        {
            var ex = await Assert.ThrowsAsync<GameActionException>(() => service.PlanActionAsync(joinCode, "user-1", MoveTo(distance, 0)));
            Assert.True(ex.IsActionRejected);
            Assert.Contains($"maximum range ({range} squares)", ex.Message);
        }
    }

    [Fact]
    public async Task PlanAction_RejectsMoveOutsideTheMapsGrid()
    {
        await using var db = CreateDb();
        var (service, redis, joinCode, id) = await SetupMover(db, 6);
        redis.Setup(r => r.GetMapAsync(It.IsAny<string>())).ReturnsAsync(
            new MapEntry(4, 4, new Dictionary<string, Position> { [id] = new Position(0, 0) }, []));

        var ex = await Assert.ThrowsAsync<GameActionException>(() => service.PlanActionAsync(joinCode, "user-1", MoveTo(4, 0)));
        Assert.Contains("out of bounds", ex.Message);
    }

    [Fact]
    public async Task PlanAction_AcceptsMoveWithinRange()
    {
        await using var db = CreateDb();
        var sessionId = Guid.NewGuid();
        var encounterId = Guid.NewGuid();
        const string userId = "user-1";
        const string joinCode = "PLAN02";
        var combatantId = Guid.NewGuid().ToString();

        db.GameSessions.Add(new GameSession
        {
            Id = sessionId,
            CampaignId = Guid.NewGuid(),
            JoinCode = joinCode,
            StartedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var activeEntry = MakeActiveEncounter(encounterId.ToString(), userId, combatantId);
        var mapEntry = new MapEntry(
            20, 20,
            new Dictionary<string, Position> { [combatantId] = new Position(0, 0) },
            new List<string>());

        var mockRedis = new Mock<IRedisGameState>();
        mockRedis.Setup(r => r.GetActiveEncounterAsync(sessionId.ToString())).ReturnsAsync(activeEntry);
        mockRedis.Setup(r => r.IsDefeatedAsync(encounterId.ToString(), combatantId)).ReturnsAsync(false);
        mockRedis.Setup(r => r.GetMapAsync(encounterId.ToString())).ReturnsAsync(mapEntry);
        mockRedis.Setup(r => r.SetPlannedActionAsync(encounterId.ToString(), combatantId, It.IsAny<string>()))
                 .Returns(Task.CompletedTask);

        var service = new PlanningService(db, mockRedis.Object, NullLogger<PlanningService>.Instance);

        var action = new PlannedActionArray(new List<PlannedActionEntry>
        {
            new("move", new PositionDto(4, 4), null),
        });

        var result = await service.PlanActionAsync(joinCode, userId, action);

        Assert.True(result.Success);
        mockRedis.Verify(r => r.SetPlannedActionAsync(encounterId.ToString(), combatantId, It.IsAny<string>()), Times.Once());
    }

    [Fact]
    public async Task PlanAction_RejectsWhenNotInPlanningPhase()
    {
        await using var db = CreateDb();
        var sessionId = Guid.NewGuid();
        var encounterId = Guid.NewGuid();
        const string userId = "user-1";
        const string joinCode = "PLAN03";
        var combatantId = Guid.NewGuid().ToString();

        db.GameSessions.Add(new GameSession
        {
            Id = sessionId,
            CampaignId = Guid.NewGuid(),
            JoinCode = joinCode,
            StartedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var resolveEntry = new ActiveEncounterEntry(
            encounterId.ToString(), "resolving", 1, DateTimeOffset.UtcNow.AddSeconds(30),
            new List<CombatantRef> { new(combatantId, userId) });

        var mockRedis = new Mock<IRedisGameState>();
        mockRedis.Setup(r => r.GetActiveEncounterAsync(sessionId.ToString())).ReturnsAsync(resolveEntry);

        var service = new PlanningService(db, mockRedis.Object, NullLogger<PlanningService>.Instance);

        var action = new PlannedActionArray(new List<PlannedActionEntry> { new("defend", null, null) });

        var ex = await Assert.ThrowsAsync<GameActionException>(
            () => service.PlanActionAsync(joinCode, userId, action));

        Assert.True(ex.IsActionRejected);
        Assert.Contains("planning phase", ex.Message);
    }

    [Fact]
    public async Task PlanEnemyAction_RejectsNonGmUser()
    {
        await using var db = CreateDb();
        var campaignId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        const string joinCode = "ENM01";

        db.GameSessions.Add(new GameSession { Id = sessionId, CampaignId = campaignId, JoinCode = joinCode, StartedAt = DateTimeOffset.UtcNow });
        db.CampaignMembers.Add(new CampaignMember { Id = Guid.NewGuid(), CampaignId = campaignId, UserId = "player-1", Role = CampaignRole.Player });
        await db.SaveChangesAsync();

        var mockRedis = new Mock<IRedisGameState>();
        var service = new PlanningService(db, mockRedis.Object, NullLogger<PlanningService>.Instance);

        var action = new PlannedActionArray(new List<PlannedActionEntry> { new("defend", null, null) });
        var ex = await Assert.ThrowsAsync<GameActionException>(
            () => service.PlanEnemyActionAsync(joinCode, "player-1", Guid.NewGuid().ToString(), action));

        Assert.True(ex.IsActionRejected);
        Assert.Contains("Only the GM", ex.Message);
    }

    [Fact]
    public async Task PlanEnemyAction_RejectsPlayerCombatant()
    {
        await using var db = CreateDb();
        const string gmUserId = "gm-1";
        const string joinCode = "ENM02";
        var (sessionId, _) = await SetupGmSession(db, joinCode, gmUserId);

        var encounterId = Guid.NewGuid().ToString();
        var playerCombatantId = Guid.NewGuid().ToString();
        // Active encounter has that combatant as a PLAYER, not monster
        var activeEntry = new ActiveEncounterEntry(encounterId, "planning", 1,
            DateTimeOffset.UtcNow.AddSeconds(30),
            new List<CombatantRef> { new(playerCombatantId, "player-1", "player") });

        var mockRedis = new Mock<IRedisGameState>();
        mockRedis.Setup(r => r.GetActiveEncounterAsync(sessionId.ToString())).ReturnsAsync(activeEntry);

        var service = new PlanningService(db, mockRedis.Object, NullLogger<PlanningService>.Instance);

        var action = new PlannedActionArray(new List<PlannedActionEntry> { new("defend", null, null) });
        var ex = await Assert.ThrowsAsync<GameActionException>(
            () => service.PlanEnemyActionAsync(joinCode, gmUserId, playerCombatantId, action));

        Assert.True(ex.IsActionRejected);
        Assert.Contains("monster combatants", ex.Message);
    }

    [Fact]
    public async Task PlanEnemyAction_StoresDefendActionForMonster()
    {
        await using var db = CreateDb();
        const string gmUserId = "gm-1";
        const string joinCode = "ENM03";
        var (sessionId, _) = await SetupGmSession(db, joinCode, gmUserId);

        var encounterId = Guid.NewGuid().ToString();
        var monsterId = Guid.NewGuid().ToString();
        var activeEntry = MakeActiveEncounterWithMonster(encounterId, gmUserId, monsterId);

        var mockRedis = new Mock<IRedisGameState>();
        mockRedis.Setup(r => r.GetActiveEncounterAsync(sessionId.ToString())).ReturnsAsync(activeEntry);
        mockRedis.Setup(r => r.IsDefeatedAsync(encounterId, monsterId)).ReturnsAsync(false);
        mockRedis.Setup(r => r.SetPlannedActionAsync(encounterId, monsterId, It.IsAny<string>())).Returns(Task.CompletedTask);

        var service = new PlanningService(db, mockRedis.Object, NullLogger<PlanningService>.Instance);

        var action = new PlannedActionArray(new List<PlannedActionEntry> { new("defend", null, null) });
        var result = await service.PlanEnemyActionAsync(joinCode, gmUserId, monsterId, action);

        Assert.True(result.Success);
        mockRedis.Verify(r => r.SetPlannedActionAsync(encounterId, monsterId, It.IsAny<string>()), Times.Once());
    }

    [Fact]
    public async Task LockInEnemies_AssignsDefaultDefendToUnplannedMonster()
    {
        await using var db = CreateDb();
        const string gmUserId = "gm-1";
        const string joinCode = "LENM01";
        var (sessionId, _) = await SetupGmSession(db, joinCode, gmUserId);

        var encounterId = Guid.NewGuid().ToString();
        var monsterId = Guid.NewGuid().ToString();
        var activeEntry = MakeActiveEncounterWithMonster(encounterId, gmUserId, monsterId);

        var mockRedis = new Mock<IRedisGameState>();
        mockRedis.Setup(r => r.GetActiveEncounterAsync(sessionId.ToString())).ReturnsAsync(activeEntry);
        mockRedis.Setup(r => r.IsDefeatedAsync(encounterId, monsterId)).ReturnsAsync(false);
        mockRedis.Setup(r => r.HasPlannedActionAsync(encounterId, monsterId)).ReturnsAsync(false);
        mockRedis.Setup(r => r.SetPlannedActionAsync(encounterId, monsterId, It.IsAny<string>())).Returns(Task.CompletedTask);
        mockRedis.Setup(r => r.AddLockedInBatchAsync(encounterId, It.IsAny<IEnumerable<string>>())).Returns(Task.CompletedTask);
        mockRedis.Setup(r => r.IsLockedInAsync(encounterId, monsterId)).ReturnsAsync(true);

        var service = new PlanningService(db, mockRedis.Object, NullLogger<PlanningService>.Instance);

        var result = await service.LockInEnemiesAsync(joinCode, gmUserId);

        Assert.True(result.AllLockedIn);
        mockRedis.Verify(r => r.SetPlannedActionAsync(encounterId, monsterId, It.Is<string>(s => s.Contains("defend"))), Times.Once());
        mockRedis.Verify(r => r.AddLockedInBatchAsync(encounterId, It.Is<IEnumerable<string>>(ids => ids.Contains(monsterId))), Times.Once());
    }

    [Fact]
    public async Task LockInEnemies_SkipsDefeatedMonsters()
    {
        await using var db = CreateDb();
        const string gmUserId = "gm-1";
        const string joinCode = "LENM02";
        var (sessionId, _) = await SetupGmSession(db, joinCode, gmUserId);

        var encounterId = Guid.NewGuid().ToString();
        var deadMonsterId = Guid.NewGuid().ToString();
        var aliveMonsterId = Guid.NewGuid().ToString();
        var activeEntry = new ActiveEncounterEntry(encounterId, "planning", 1,
            DateTimeOffset.UtcNow.AddSeconds(30), new List<CombatantRef>
            {
                new(deadMonsterId, gmUserId, "monster"),
                new(aliveMonsterId, gmUserId, "monster"),
            });

        var mockRedis = new Mock<IRedisGameState>();
        mockRedis.Setup(r => r.GetActiveEncounterAsync(sessionId.ToString())).ReturnsAsync(activeEntry);
        mockRedis.Setup(r => r.IsDefeatedAsync(encounterId, deadMonsterId)).ReturnsAsync(true);
        mockRedis.Setup(r => r.IsDefeatedAsync(encounterId, aliveMonsterId)).ReturnsAsync(false);
        mockRedis.Setup(r => r.HasPlannedActionAsync(encounterId, aliveMonsterId)).ReturnsAsync(true);
        mockRedis.Setup(r => r.AddLockedInBatchAsync(encounterId, It.IsAny<IEnumerable<string>>())).Returns(Task.CompletedTask);
        mockRedis.Setup(r => r.IsLockedInAsync(encounterId, deadMonsterId)).ReturnsAsync(false);
        mockRedis.Setup(r => r.IsLockedInAsync(encounterId, aliveMonsterId)).ReturnsAsync(true);

        var service = new PlanningService(db, mockRedis.Object, NullLogger<PlanningService>.Instance);

        await service.LockInEnemiesAsync(joinCode, gmUserId);

        // Defeated monster must NOT be locked in
        mockRedis.Verify(r => r.AddLockedInBatchAsync(encounterId,
            It.Is<IEnumerable<string>>(ids => ids.Contains(aliveMonsterId) && !ids.Contains(deadMonsterId))),
            Times.Once());
    }

    [Fact]
    public async Task ForceResolve_AssignsDefendAndLocksInUnplannedCombatants()
    {
        await using var db = CreateDb();
        const string gmUserId = "gm-1";
        const string joinCode = "FORCE01";
        var (sessionId, _) = await SetupGmSession(db, joinCode, gmUserId);

        var encounterId = Guid.NewGuid().ToString();
        var lockedCombatantId = Guid.NewGuid().ToString();
        var unlockedCombatantId = Guid.NewGuid().ToString();
        var activeEntry = new ActiveEncounterEntry(encounterId, "planning", 1,
            DateTimeOffset.UtcNow.AddSeconds(30), new List<CombatantRef>
            {
                new(lockedCombatantId, "player-1", "player"),
                new(unlockedCombatantId, "player-2", "player"),
            });

        var mockRedis = new Mock<IRedisGameState>();
        mockRedis.Setup(r => r.GetActiveEncounterAsync(sessionId.ToString())).ReturnsAsync(activeEntry);
        mockRedis.Setup(r => r.IsDefeatedAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);
        mockRedis.Setup(r => r.HasPlannedActionAsync(encounterId, lockedCombatantId)).ReturnsAsync(true);
        mockRedis.Setup(r => r.HasPlannedActionAsync(encounterId, unlockedCombatantId)).ReturnsAsync(false);
        mockRedis.Setup(r => r.IsLockedInAsync(encounterId, lockedCombatantId)).ReturnsAsync(true);
        mockRedis.Setup(r => r.IsLockedInAsync(encounterId, unlockedCombatantId)).ReturnsAsync(false);
        mockRedis.Setup(r => r.SetPlannedActionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        mockRedis.Setup(r => r.AddLockedInBatchAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>())).Returns(Task.CompletedTask);

        var service = new PlanningService(db, mockRedis.Object, NullLogger<PlanningService>.Instance);

        var result = await service.ForceResolveAsync(joinCode, gmUserId);

        Assert.True(result.AllLockedIn);
        // Only the unlocked combatant should get a defend action
        mockRedis.Verify(r => r.SetPlannedActionAsync(encounterId, unlockedCombatantId, It.Is<string>(s => s.Contains("defend"))), Times.Once());
        mockRedis.Verify(r => r.SetPlannedActionAsync(encounterId, lockedCombatantId, It.IsAny<string>()), Times.Never());
        // Only the unlocked combatant is batch-locked
        mockRedis.Verify(r => r.AddLockedInBatchAsync(encounterId,
            It.Is<IEnumerable<string>>(ids => ids.Contains(unlockedCombatantId) && !ids.Contains(lockedCombatantId))),
            Times.Once());
    }

    [Fact]
    public async Task LockIn_RejectsWithoutPlannedAction()
    {
        await using var db = CreateDb();
        var sessionId = Guid.NewGuid();
        var encounterId = Guid.NewGuid();
        const string userId = "user-1";
        const string joinCode = "LOCK01";
        var combatantId = Guid.NewGuid().ToString();

        db.GameSessions.Add(new GameSession
        {
            Id = sessionId,
            CampaignId = Guid.NewGuid(),
            JoinCode = joinCode,
            StartedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var activeEntry = MakeActiveEncounter(encounterId.ToString(), userId, combatantId);

        var mockRedis = new Mock<IRedisGameState>();
        mockRedis.Setup(r => r.GetActiveEncounterAsync(sessionId.ToString())).ReturnsAsync(activeEntry);
        mockRedis.Setup(r => r.HasPlannedActionAsync(encounterId.ToString(), combatantId)).ReturnsAsync(false);

        var service = new PlanningService(db, mockRedis.Object, NullLogger<PlanningService>.Instance);

        var ex = await Assert.ThrowsAsync<GameActionException>(
            () => service.LockInAsync(joinCode, userId));

        Assert.True(ex.IsActionRejected);
        Assert.Contains("plan an action", ex.Message);
    }

    // ----- Several characters per user (Quick Battle) -----

    private sealed class PartyFixture
    {
        public const string UserId = "qb-user";
        public const string JoinCode = "PARTY1";
        public readonly AppDbContext Db;
        public readonly Guid SessionId = Guid.NewGuid();
        public readonly string EncounterId = Guid.NewGuid().ToString();
        public readonly string Bruiser = Guid.NewGuid().ToString();
        public readonly string Scout = Guid.NewGuid().ToString();
        public readonly string Goblin = Guid.NewGuid().ToString();
        public readonly Mock<IRedisGameState> Redis = new();
        public readonly Dictionary<string, string> Plans = [];
        public readonly HashSet<string> Locked = [];
        public readonly HashSet<string> Defeated = [];

        public PartyFixture()
        {
            Db = CreateDb();
            Db.GameSessions.Add(new GameSession { Id = SessionId, CampaignId = Guid.NewGuid(), JoinCode = JoinCode, StartedAt = DateTimeOffset.UtcNow });
            var encounterGuid = Guid.Parse(EncounterId);
            foreach (var (id, name, type) in new[] { (Bruiser, "Bruiser", "player"), (Scout, "Scout", "player"), (Goblin, "Goblin", "monster") })
                Db.EncounterCombatants.Add(new EncounterCombatant { Id = Guid.Parse(id), EncounterId = encounterGuid, UserId = UserId, CharacterName = name, CombatantType = type });
            Db.SaveChanges();

            // The user owns the goblin's row too (Quick Battle), but it's a monster: not theirs to plan.
            var active = new ActiveEncounterEntry(EncounterId, "planning", 1, DateTimeOffset.UtcNow, new List<CombatantRef>
            {
                new(Bruiser, UserId, "player"),
                new(Scout, UserId, "player"),
                new(Goblin, UserId, "monster", IsAiControlled: true),
            }, PlanningTimerEnabled: false);
            Redis.Setup(r => r.GetActiveEncounterAsync(SessionId.ToString())).ReturnsAsync(active);
            Redis.Setup(r => r.GetMapAsync(EncounterId)).ReturnsAsync(new MapEntry(20, 20, new Dictionary<string, Position>
            {
                [Bruiser] = new(8, 2), [Scout] = new(12, 2), [Goblin] = new(8, 14),
            }, []));
            Redis.Setup(r => r.IsDefeatedAsync(EncounterId, It.IsAny<string>())).ReturnsAsync((string _, string id) => Defeated.Contains(id));
            Redis.Setup(r => r.HasPlannedActionAsync(EncounterId, It.IsAny<string>())).ReturnsAsync((string _, string id) => Plans.ContainsKey(id));
            Redis.Setup(r => r.SetPlannedActionAsync(EncounterId, It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string, string>((_, id, json) => Plans[id] = json).Returns(Task.CompletedTask);
            Redis.Setup(r => r.IsLockedInAsync(EncounterId, It.IsAny<string>())).ReturnsAsync((string _, string id) => Locked.Contains(id));
            Redis.Setup(r => r.AddLockedInAsync(EncounterId, It.IsAny<string>()))
                .Callback<string, string>((_, id) => Locked.Add(id)).Returns(Task.CompletedTask);
            Redis.Setup(r => r.AddLockedInBatchAsync(EncounterId, It.IsAny<IEnumerable<string>>()))
                .Callback<string, IEnumerable<string>>((_, ids) => Locked.UnionWith(ids)).Returns(Task.CompletedTask);
        }

        public PlanningService Service() => new(Db, Redis.Object, NullLogger<PlanningService>.Instance);
    }

    private static PlannedActionArray Move(int x, int y) => new([new PlannedActionEntry("move", new PositionDto(x, y), null)]);

    [Fact]
    public async Task PlanAction_ForOneOfSeveralOwnedCharacters_StoresItUnderThatCharacter()
    {
        var f = new PartyFixture();
        await f.Service().PlanActionAsync(PartyFixture.JoinCode, PartyFixture.UserId, Move(12, 5), f.Scout);
        Assert.Equal([f.Scout], f.Plans.Keys);
    }

    [Fact]
    public async Task PlanAction_WithSeveralCharactersButNoCombatantId_IsRejected()
    {
        var f = new PartyFixture();
        var ex = await Assert.ThrowsAsync<GameActionException>(
            () => f.Service().PlanActionAsync(PartyFixture.JoinCode, PartyFixture.UserId, Move(8, 5)));
        Assert.Contains("several characters", ex.Message);
    }

    [Fact]
    public async Task PlanAction_ForAMonsterTheUserOwnsTheRowOf_IsRejected()
    {
        var f = new PartyFixture();
        var ex = await Assert.ThrowsAsync<GameActionException>(
            () => f.Service().PlanActionAsync(PartyFixture.JoinCode, PartyFixture.UserId, Move(8, 12), f.Goblin));
        Assert.Contains("don't control", ex.Message);
        Assert.Empty(f.Plans);
    }

    [Fact]
    public async Task PlanAction_ForSomeoneElsesCharacter_IsRejected()
    {
        var f = new PartyFixture();
        var ex = await Assert.ThrowsAsync<GameActionException>(
            () => f.Service().PlanActionAsync(PartyFixture.JoinCode, "another-user", Move(8, 5), f.Bruiser));
        Assert.Contains("not in this encounter", ex.Message);
    }

    [Fact]
    public async Task LockIn_LocksEveryLivingCharacter_UnplannedOnesDefend()
    {
        var f = new PartyFixture();
        await f.Service().PlanActionAsync(PartyFixture.JoinCode, PartyFixture.UserId, Move(8, 5), f.Bruiser);
        f.Locked.Add(f.Goblin); // AI already locked in

        var result = await f.Service().LockInAsync(PartyFixture.JoinCode, PartyFixture.UserId);

        Assert.True(result.AllLockedIn);
        Assert.Equal(["Bruiser", "Scout"], result.LockedInNames);
        Assert.Contains("defend", f.Plans[f.Scout]);
        Assert.Contains(f.Bruiser, f.Locked);
        Assert.Contains(f.Scout, f.Locked);
    }

    [Fact]
    public async Task LockIn_SkipsDefeatedCharacters()
    {
        var f = new PartyFixture();
        f.Defeated.Add(f.Scout);
        await f.Service().PlanActionAsync(PartyFixture.JoinCode, PartyFixture.UserId, Move(8, 5), f.Bruiser);

        var result = await f.Service().LockInAsync(PartyFixture.JoinCode, PartyFixture.UserId);

        Assert.Equal(["Bruiser"], result.LockedInNames);
        Assert.DoesNotContain(f.Scout, f.Locked);
        Assert.False(f.Plans.ContainsKey(f.Scout));
    }

    [Fact]
    public async Task LockIn_WithNothingPlanned_IsRejected()
    {
        var f = new PartyFixture();
        var ex = await Assert.ThrowsAsync<GameActionException>(
            () => f.Service().LockInAsync(PartyFixture.JoinCode, PartyFixture.UserId));
        Assert.Contains("plan an action", ex.Message);
        Assert.Empty(f.Locked);
    }
}
