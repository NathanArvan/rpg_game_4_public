using Microsoft.EntityFrameworkCore;
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

        var service = new PlanningService(db, mockRedis.Object);

        var action = new PlannedActionArray(new List<PlannedActionEntry>
        {
            new("move", new PositionDto(0, 7), null),
        });

        var ex = await Assert.ThrowsAsync<GameActionException>(
            () => service.PlanActionAsync(joinCode, userId, action));

        Assert.True(ex.IsActionRejected);
        Assert.Contains("maximum range", ex.Message);
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

        var service = new PlanningService(db, mockRedis.Object);

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

        var service = new PlanningService(db, mockRedis.Object);

        var action = new PlannedActionArray(new List<PlannedActionEntry> { new("defend", null, null) });

        var ex = await Assert.ThrowsAsync<GameActionException>(
            () => service.PlanActionAsync(joinCode, userId, action));

        Assert.True(ex.IsActionRejected);
        Assert.Contains("planning phase", ex.Message);
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

        var service = new PlanningService(db, mockRedis.Object);

        var ex = await Assert.ThrowsAsync<GameActionException>(
            () => service.LockInAsync(joinCode, userId));

        Assert.True(ex.IsActionRejected);
        Assert.Contains("plan an action", ex.Message);
    }
}
