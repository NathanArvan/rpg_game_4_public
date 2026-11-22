using Microsoft.EntityFrameworkCore;
using Moq;
using RpgGame.Api.Models;
using RpgGame.Api.Services;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;

namespace RpgGame.Api.Tests;

public class EncounterStateLoaderTests
{
    [Fact]
    public async Task Combatants_load_in_creation_order_not_by_id()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var encounterId = Guid.NewGuid();
        // Ids sort the other way round from the creation order.
        var first = new EncounterCombatant { Id = Guid.Parse("ffffffff-0000-0000-0000-000000000000"), CharacterName = "First", SortOrder = 0 };
        var second = new EncounterCombatant { Id = Guid.Parse("88888888-0000-0000-0000-000000000000"), CharacterName = "Second", SortOrder = 1 };
        var third = new EncounterCombatant { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), CharacterName = "Third", SortOrder = 2 };
        db.Encounters.Add(new Encounter { Id = encounterId, Combatants = [third, first, second] });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var redis = new Mock<IRedisGameState>();
        redis.Setup(r => r.GetMapAsync(encounterId.ToString())).ReturnsAsync(new MapEntry(20, 20, [], []));
        redis.Setup(r => r.GetCooldownsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync([]);

        var loaded = await EncounterStateLoader.LoadAsync(db, redis.Object, encounterId);

        Assert.Equal(["First", "Second", "Third"], loaded.Encounter.Combatants.Select(c => c.CharacterName));
        Assert.Equal([first.Id.ToString(), second.Id.ToString(), third.Id.ToString()], loaded.Records.Keys);
    }
}
