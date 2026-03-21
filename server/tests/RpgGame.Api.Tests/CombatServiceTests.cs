using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RpgGame.Api.Models;
using RpgGame.Api.Services;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;

namespace RpgGame.Api.Tests;

public class CombatServiceTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task DeclareAttack_RejectsWhenCallerNotInEncounter()
    {
        await using var db = CreateDb();
        var sessionId = Guid.NewGuid();
        var encounterId = Guid.NewGuid();
        const string userId = "test-user";
        const string joinCode = "ATK001";
        var activeCombatantId = Guid.NewGuid().ToString();
        var targetCombatantId = Guid.NewGuid().ToString();

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
            "planning",
            1,
            DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(),
            new List<CombatantRef>
            {
                new(activeCombatantId, "other-user"),
                new(targetCombatantId, "another-user"),
            });

        var mockRedis = new Mock<IRedisGameState>();
        mockRedis.Setup(r => r.GetActiveEncounterAsync(sessionId.ToString()))
                 .ReturnsAsync(activeEncounterEntry);

        var service = new CombatService(db, mockRedis.Object, NullLogger<CombatService>.Instance);

        var ex = await Assert.ThrowsAsync<GameActionException>(
            () => service.DeclareAttackAsync(joinCode, userId, targetCombatantId));

        Assert.Contains("not in this encounter", ex.Message);
        Assert.True(ex.IsActionRejected);
    }
}
