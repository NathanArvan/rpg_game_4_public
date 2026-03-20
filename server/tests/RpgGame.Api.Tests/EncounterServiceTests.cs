using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RpgGame.Api.Contracts.Hub;
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
        var service = new EncounterService(db, mockRedis.Object);

        var ex = await Assert.ThrowsAsync<GameActionException>(
            () => service.StartEncounterAsync(joinCode, userId));

        Assert.Contains("Only the GM", ex.Message);
    }

    [Fact]
    public async Task EndTurn_WhenAllDefeated_ReturnsEncounterEndedResult()
    {
        await using var db = CreateDb();
        var sessionId = Guid.NewGuid();
        var encounterId = Guid.NewGuid();
        const string userId = "user-1";
        const string joinCode = "END001";
        var combatantId1 = Guid.NewGuid().ToString();
        var combatantId2 = Guid.NewGuid().ToString();

        db.GameSessions.Add(new GameSession
        {
            Id = sessionId,
            CampaignId = Guid.NewGuid(),
            JoinCode = joinCode,
            StartedAt = DateTimeOffset.UtcNow,
        });
        db.Encounters.Add(new Encounter
        {
            Id = encounterId,
            GameSessionId = sessionId,
            Status = EncounterStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var combatants = new List<CombatantRef>
        {
            new(combatantId1, userId),
            new(combatantId2, "other-user"),
        };
        var activeEncounterEntry = new ActiveEncounterEntry(encounterId.ToString(), combatantId1, combatants);

        var mockRedis = new Mock<IRedisGameState>();
        mockRedis.Setup(r => r.GetActiveEncounterAsync(sessionId.ToString()))
                 .ReturnsAsync(activeEncounterEntry);
        mockRedis.Setup(r => r.IsDefeatedAsync(encounterId.ToString(), It.IsAny<string>()))
                 .ReturnsAsync(true);
        mockRedis.Setup(r => r.DeleteActiveEncounterAsync(sessionId.ToString()))
                 .Returns(Task.CompletedTask);

        var service = new EncounterService(db, mockRedis.Object);

        var result = await service.EndTurnAsync(joinCode, userId);

        Assert.IsType<EncounterEndedResult>(result);
        mockRedis.Verify(r => r.DeleteActiveEncounterAsync(sessionId.ToString()), Times.Once());
        mockRedis.Verify(r => r.SetActiveEncounterAsync(It.IsAny<string>(), It.IsAny<ActiveEncounterEntry>()), Times.Never());
    }

    [Fact]
    public async Task MoveToken_RejectsChebyshevGreaterThanSix()
    {
        await using var db = CreateDb();
        var sessionId = Guid.NewGuid();
        var encounterId = Guid.NewGuid();
        const string userId = "user-1";
        const string joinCode = "MOV001";
        var combatantId = Guid.NewGuid().ToString();

        db.GameSessions.Add(new GameSession
        {
            Id = sessionId,
            CampaignId = Guid.NewGuid(),
            JoinCode = joinCode,
            StartedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var activeEncounterEntry = new ActiveEncounterEntry(
            encounterId.ToString(),
            combatantId,
            new List<CombatantRef> { new(combatantId, userId) });

        var mapEntry = new MapEntry(
            20, 20,
            new Dictionary<string, Position> { [combatantId] = new Position(0, 0) },
            new List<string>());

        var mockRedis = new Mock<IRedisGameState>();
        mockRedis.Setup(r => r.GetActiveEncounterAsync(sessionId.ToString()))
                 .ReturnsAsync(activeEncounterEntry);
        mockRedis.Setup(r => r.GetMapAsync(encounterId.ToString()))
                 .ReturnsAsync(mapEntry);

        var service = new EncounterService(db, mockRedis.Object);

        var ex = await Assert.ThrowsAsync<GameActionException>(
            () => service.MoveTokenAsync(joinCode, userId, combatantId, 0, 7));

        Assert.True(ex.IsActionRejected);
    }
}
