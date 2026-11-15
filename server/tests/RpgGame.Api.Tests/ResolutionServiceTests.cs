using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Models;
using RpgGame.Api.Services;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;
using RpgGame.Rules;
using System.Text.Json;

namespace RpgGame.Api.Tests;

public partial class ResolutionServiceTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static string ActionJson(string type, PositionDto? targetCell = null, string? targetId = null) =>
        JsonSerializer.Serialize(
            new PlannedActionArray(new List<PlannedActionEntry> { new(type, targetCell, targetId) }),
            JsonOpts);

    // Sets up Redis mock with all write methods no-op'd and provides planned actions + map.
    private static Mock<IRedisGameState> BuildMockRedis(
        string encounterIdStr,
        string sessionIdStr,
        Dictionary<string, string> plannedActions,
        MapEntry mapEntry,
        Dictionary<string, int>? hpByKey = null,
        HashSet<string>? preDefeated = null)
    {
        var mock = new Mock<IRedisGameState>();

        mock.Setup(r => r.GetAllPlannedActionsAsync(encounterIdStr)).ReturnsAsync(plannedActions);
        mock.Setup(r => r.GetMapAsync(encounterIdStr)).ReturnsAsync(mapEntry);
        mock.Setup(r => r.GetActiveEncounterAsync(sessionIdStr)).ReturnsAsync(
            new ActiveEncounterEntry(encounterIdStr, "planning", 1, DateTimeOffset.UtcNow.AddSeconds(30), new List<CombatantRef>()));

        // HP defaults to 20 unless overridden
        mock.Setup(r => r.GetCharacterHpAsync(It.IsAny<string>())).ReturnsAsync(20);
        if (hpByKey is not null)
            foreach (var (key, hp) in hpByKey)
                mock.Setup(r => r.GetCharacterHpAsync(key)).ReturnsAsync(hp);

        // Defeated defaults to false unless overridden
        mock.Setup(r => r.IsDefeatedAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);
        if (preDefeated is not null)
            foreach (var id in preDefeated)
                mock.Setup(r => r.IsDefeatedAsync(encounterIdStr, id)).ReturnsAsync(true);

        // Write ops
        mock.Setup(r => r.SetHpAsync(It.IsAny<string>(), It.IsAny<long>())).Returns(Task.CompletedTask);
        mock.Setup(r => r.MarkDefeatedAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        mock.Setup(r => r.SetMapAsync(It.IsAny<string>(), It.IsAny<MapEntry>())).Returns(Task.CompletedTask);
        mock.Setup(r => r.ClearPlannedActionsAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        mock.Setup(r => r.ClearLockedInAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        mock.Setup(r => r.SetActiveEncounterAsync(It.IsAny<string>(), It.IsAny<ActiveEncounterEntry>())).Returns(Task.CompletedTask);

        return mock;
    }

    // Adds an Encounter with the given monster combatants to an InMemory DB.
    private static async Task<Guid> SeedEncounterWithMonsters(
        AppDbContext db, Guid sessionId, params (Guid id, string name)[] monsters)
    {
        var encounterId = Guid.NewGuid();
        var combatants = monsters.Select(m => new EncounterCombatant
        {
            Id = m.id,
            EncounterId = encounterId,
            UserId = "gm",
            CharacterId = null,
            CharacterName = m.name,
            CombatantType = "monster",
        }).ToList();

        db.Encounters.Add(new Encounter
        {
            Id = encounterId,
            GameSessionId = sessionId,
            Status = EncounterStatus.Active,
            RoundNumber = 1,
            StartedAt = DateTimeOffset.UtcNow,
            Combatants = combatants,
        });
        await db.SaveChangesAsync();
        return encounterId;
    }

    // Adds an Encounter with one player combatant (and the backing Character) plus optional monsters.
    private static async Task<(Guid encounterId, Guid characterId, Guid playerCombatantId)> SeedEncounterWithPlayer(
        AppDbContext db, Guid sessionId, string userId, int dex, params (Guid id, string name)[] monsters)
    {
        var characterId = Guid.NewGuid();
        db.Characters.Add(new Character
        {
            Id = characterId,
            UserId = userId,
            Name = "Frodo",
            Race = "Hobbit",
            Class = "fighter",
            Level = 1,
            MaxHp = 20,
            CurrentHp = 20,
            BaseStats = new BaseStats { Dex = dex },
        });

        var encounterId = Guid.NewGuid();
        var playerCombatantId = Guid.NewGuid();

        var combatants = new List<EncounterCombatant>
        {
            new() { Id = playerCombatantId, EncounterId = encounterId, UserId = userId, CharacterId = characterId, CharacterName = "Frodo", CombatantType = "player" },
        };
        combatants.AddRange(monsters.Select(m => new EncounterCombatant
        {
            Id = m.id,
            EncounterId = encounterId,
            UserId = "gm",
            CharacterId = null,
            CharacterName = m.name,
            CombatantType = "monster",
        }));

        db.Encounters.Add(new Encounter
        {
            Id = encounterId,
            GameSessionId = sessionId,
            Status = EncounterStatus.Active,
            RoundNumber = 1,
            StartedAt = DateTimeOffset.UtcNow,
            Combatants = combatants,
        });
        await db.SaveChangesAsync();
        return (encounterId, characterId, playerCombatantId);
    }

    // ----- Movement phase -----

    [Fact]
    public async Task ResolveRound_MovesMonsterToTargetCell()
    {
        await using var db = CreateDb();
        var sessionId = Guid.NewGuid();
        var monsterId = Guid.NewGuid();
        var encounterId = await SeedEncounterWithMonsters(db, sessionId, (monsterId, "Goblin"));
        var encounterIdStr = encounterId.ToString();

        var mapEntry = new MapEntry(20, 20,
            new Dictionary<string, Position> { [monsterId.ToString()] = new(0, 0) },
            new List<string>());

        var plannedActions = new Dictionary<string, string>
        {
            [monsterId.ToString()] = ActionJson("move", new PositionDto(3, 0)),
        };

        var mockRedis = BuildMockRedis(encounterIdStr, sessionId.ToString(), plannedActions, mapEntry);
        var service = new ResolutionService(db, mockRedis.Object, new ScriptedDice(), NullLogger<ResolutionService>.Instance);

        var result = await service.ResolveRoundAsync(encounterId);

        var movement = Assert.Single(result.Ticks[0].Movements);
        Assert.Equal(monsterId.ToString(), movement.CombatantId);
        Assert.Equal(3, movement.FinalPosition.X);
        Assert.Equal(0, movement.FinalPosition.Y);
        Assert.False(movement.WasBlocked);
    }

    [Fact]
    public async Task ResolveRound_HigherDexWinsContestedCell()
    {
        await using var db = CreateDb();
        var sessionId = Guid.NewGuid();
        var monsterId = Guid.NewGuid();
        // Player has Dex 14, monster defaults to 10
        var (encounterId, characterId, playerCombatantId) =
            await SeedEncounterWithPlayer(db, sessionId, "user-1", dex: 14, (monsterId, "Goblin"));

        var encounterIdStr = encounterId.ToString();
        var monsterIdStr = monsterId.ToString();
        var playerCombatantIdStr = playerCombatantId.ToString();

        // Both target cell (1, 0); player at (0,0), monster at (2,0)
        var mapEntry = new MapEntry(20, 20,
            new Dictionary<string, Position>
            {
                [playerCombatantIdStr] = new(0, 0),
                [monsterIdStr] = new(2, 0),
            },
            new List<string>());

        var plannedActions = new Dictionary<string, string>
        {
            [playerCombatantIdStr] = ActionJson("move", new PositionDto(1, 0)),
            [monsterIdStr] = ActionJson("move", new PositionDto(1, 0)),
        };

        var mockRedis = BuildMockRedis(encounterIdStr, sessionId.ToString(), plannedActions, mapEntry,
            hpByKey: new Dictionary<string, int> { [characterId.ToString()] = 20, [monsterIdStr] = 20 });
        var service = new ResolutionService(db, mockRedis.Object, new ScriptedDice(), NullLogger<ResolutionService>.Instance);

        var result = await service.ResolveRoundAsync(encounterId);

        var movements = result.Ticks[0].Movements;
        var playerMove = movements.First(m => m.CombatantId == playerCombatantIdStr);
        var monsterMove = movements.First(m => m.CombatantId == monsterIdStr);

        Assert.Equal(1, playerMove.FinalPosition.X);
        Assert.False(playerMove.WasBlocked);
        Assert.True(monsterMove.WasBlocked);
        Assert.Equal(2, monsterMove.FinalPosition.X); // stayed at start
    }

    [Fact]
    public async Task ResolveRound_TiedDexBothStopAtContestedCell()
    {
        await using var db = CreateDb();
        var sessionId = Guid.NewGuid();
        var monsterA = Guid.NewGuid();
        var monsterB = Guid.NewGuid();
        var encounterId = await SeedEncounterWithMonsters(db, sessionId, (monsterA, "Goblin A"), (monsterB, "Goblin B"));
        var encounterIdStr = encounterId.ToString();

        // Both monsters target (1, 0); both have default Dex 10
        var mapEntry = new MapEntry(20, 20,
            new Dictionary<string, Position>
            {
                [monsterA.ToString()] = new(0, 0),
                [monsterB.ToString()] = new(2, 0),
            },
            new List<string>());

        var plannedActions = new Dictionary<string, string>
        {
            [monsterA.ToString()] = ActionJson("move", new PositionDto(1, 0)),
            [monsterB.ToString()] = ActionJson("move", new PositionDto(1, 0)),
        };

        var mockRedis = BuildMockRedis(encounterIdStr, sessionId.ToString(), plannedActions, mapEntry);
        var service = new ResolutionService(db, mockRedis.Object, new ScriptedDice(), NullLogger<ResolutionService>.Instance);

        var result = await service.ResolveRoundAsync(encounterId);

        var movements = result.Ticks[0].Movements;
        Assert.All(movements, m => Assert.True(m.WasBlocked));
        var moveA = movements.First(m => m.CombatantId == monsterA.ToString());
        var moveB = movements.First(m => m.CombatantId == monsterB.ToString());
        Assert.Equal(0, moveA.FinalPosition.X); // stayed at start
        Assert.Equal(2, moveB.FinalPosition.X);
    }

    // ----- Attack phase -----

    [Fact]
    public async Task ResolveRound_MeleeAttackWastedWhenTargetNotAdjacent()
    {
        await using var db = CreateDb();
        var sessionId = Guid.NewGuid();
        var attacker = Guid.NewGuid();
        var target = Guid.NewGuid();
        var encounterId = await SeedEncounterWithMonsters(db, sessionId, (attacker, "Goblin A"), (target, "Goblin B"));
        var encounterIdStr = encounterId.ToString();

        // Attacker at (0,0), target at (5,0) — Chebyshev distance 5, not adjacent
        var mapEntry = new MapEntry(20, 20,
            new Dictionary<string, Position>
            {
                [attacker.ToString()] = new(0, 0),
                [target.ToString()] = new(5, 0),
            },
            new List<string>());

        var plannedActions = new Dictionary<string, string>
        {
            [attacker.ToString()] = ActionJson("melee_attack", targetId: target.ToString()),
        };

        var mockRedis = BuildMockRedis(encounterIdStr, sessionId.ToString(), plannedActions, mapEntry);
        var service = new ResolutionService(db, mockRedis.Object, new ScriptedDice(), NullLogger<ResolutionService>.Instance);

        var result = await service.ResolveRoundAsync(encounterId);

        var attack = Assert.Single(result.Ticks[0].Attacks);
        Assert.True(attack.Wasted);
        Assert.Contains("out of melee range", attack.FeedText);
    }

    [Fact]
    public async Task ResolveRound_MeleeAttackNotWastedWhenAdjacent()
    {
        await using var db = CreateDb();
        var sessionId = Guid.NewGuid();
        var attacker = Guid.NewGuid();
        var target = Guid.NewGuid();
        var encounterId = await SeedEncounterWithMonsters(db, sessionId, (attacker, "Goblin A"), (target, "Goblin B"));
        var encounterIdStr = encounterId.ToString();

        // Adjacent cells: Chebyshev distance 1
        var mapEntry = new MapEntry(20, 20,
            new Dictionary<string, Position>
            {
                [attacker.ToString()] = new(0, 0),
                [target.ToString()] = new(1, 0),
            },
            new List<string>());

        var plannedActions = new Dictionary<string, string>
        {
            [attacker.ToString()] = ActionJson("melee_attack", targetId: target.ToString()),
        };

        var mockRedis = BuildMockRedis(encounterIdStr, sessionId.ToString(), plannedActions, mapEntry);
        var service = new ResolutionService(db, mockRedis.Object, new ScriptedDice(15, 3), NullLogger<ResolutionService>.Instance);

        var result = await service.ResolveRoundAsync(encounterId);

        var attack = Assert.Single(result.Ticks[0].Attacks);
        Assert.False(attack.Wasted);
        Assert.Equal(attacker.ToString(), attack.AttackerId);
        Assert.Equal(target.ToString(), attack.TargetId);
    }

    // ----- Effects phase -----

    [Fact]
    public async Task ResolveRound_DefendProducesEffectResult()
    {
        await using var db = CreateDb();
        var sessionId = Guid.NewGuid();
        var monsterId = Guid.NewGuid();
        var encounterId = await SeedEncounterWithMonsters(db, sessionId, (monsterId, "Goblin"));
        var encounterIdStr = encounterId.ToString();

        var mapEntry = new MapEntry(20, 20,
            new Dictionary<string, Position> { [monsterId.ToString()] = new(0, 0) },
            new List<string>());

        var plannedActions = new Dictionary<string, string>
        {
            [monsterId.ToString()] = ActionJson("defend"),
        };

        var mockRedis = BuildMockRedis(encounterIdStr, sessionId.ToString(), plannedActions, mapEntry);
        var service = new ResolutionService(db, mockRedis.Object, new ScriptedDice(), NullLogger<ResolutionService>.Instance);

        var result = await service.ResolveRoundAsync(encounterId);

        var effect = Assert.Single(result.Ticks[0].Effects);
        Assert.Equal(monsterId.ToString(), effect.CombatantId);
        Assert.Equal("defend", effect.EffectType);
    }

    // ----- Round bookkeeping -----

    [Fact]
    public async Task ResolveRound_IncrementsRoundNumber()
    {
        await using var db = CreateDb();
        var sessionId = Guid.NewGuid();
        var monsterId = Guid.NewGuid();
        var encounterId = await SeedEncounterWithMonsters(db, sessionId, (monsterId, "Goblin"));
        var encounterIdStr = encounterId.ToString();

        var mapEntry = new MapEntry(20, 20,
            new Dictionary<string, Position> { [monsterId.ToString()] = new(0, 0) },
            new List<string>());

        var mockRedis = BuildMockRedis(encounterIdStr, sessionId.ToString(), new Dictionary<string, string>(), mapEntry);
        var service = new ResolutionService(db, mockRedis.Object, new ScriptedDice(), NullLogger<ResolutionService>.Instance);

        var result = await service.ResolveRoundAsync(encounterId);

        Assert.Equal(2, result.NewRoundNumber); // was round 1, increments to 2
    }

    [Fact]
    public async Task ResolveRound_DefeatedCombatantAttackIsSkipped()
    {
        await using var db = CreateDb();
        var sessionId = Guid.NewGuid();
        var defeatedId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var encounterId = await SeedEncounterWithMonsters(db, sessionId, (defeatedId, "Dead Goblin"), (targetId, "Alive Goblin"));
        var encounterIdStr = encounterId.ToString();

        var mapEntry = new MapEntry(20, 20,
            new Dictionary<string, Position>
            {
                [defeatedId.ToString()] = new(0, 0),
                [targetId.ToString()] = new(1, 0),
            },
            new List<string>());

        var plannedActions = new Dictionary<string, string>
        {
            [defeatedId.ToString()] = ActionJson("melee_attack", targetId: targetId.ToString()),
        };

        var mockRedis = BuildMockRedis(encounterIdStr, sessionId.ToString(), plannedActions, mapEntry,
            preDefeated: new HashSet<string> { defeatedId.ToString() });
        var service = new ResolutionService(db, mockRedis.Object, new ScriptedDice(), NullLogger<ResolutionService>.Instance);

        var result = await service.ResolveRoundAsync(encounterId);

        Assert.Empty(result.Ticks[0].Attacks);
    }

    [Fact]
    public async Task ResolveRound_PlanForUnknownCombatant_IsIgnored()
    {
        // Intentional deviation in the rules-engine extraction: PlanningService never stores plans
        // for non-combatants, but if one appears the adapter now drops it instead of resolving it
        // as an "Unknown" Dex-10 / d4 combatant.
        await using var db = CreateDb();
        var sessionId = Guid.NewGuid();
        var monsterId = Guid.NewGuid();
        var encounterId = await SeedEncounterWithMonsters(db, sessionId, (monsterId, "Goblin"));
        var encounterIdStr = encounterId.ToString();

        var mapEntry = new MapEntry(20, 20,
            new Dictionary<string, Position> { [monsterId.ToString()] = new(5, 5) },
            new List<string>());

        var plannedActions = new Dictionary<string, string>
        {
            [Guid.NewGuid().ToString()] = ActionJson("move", new PositionDto(3, 3)),
            [monsterId.ToString()] = ActionJson("move", new PositionDto(6, 5)),
        };

        var mockRedis = BuildMockRedis(encounterIdStr, sessionId.ToString(), plannedActions, mapEntry);
        var service = new ResolutionService(db, mockRedis.Object, new ScriptedDice(), NullLogger<ResolutionService>.Instance);

        var result = await service.ResolveRoundAsync(encounterId);

        var movement = Assert.Single(result.Ticks[0].Movements);
        Assert.Equal(monsterId.ToString(), movement.CombatantId);
    }

    [Fact]
    public async Task ResolveRound_UsesPresetStatOverrides_ForCombatantsWithoutACharacter()
    {
        // Quick Battle: a "player" combatant with no Character row, stats stored on the combatant.
        await using var db = CreateDb();
        var sessionId = Guid.NewGuid();
        var encounterId = Guid.NewGuid();
        var scoutId = Guid.NewGuid();
        var goblinId = Guid.NewGuid();
        db.Encounters.Add(new Encounter
        {
            Id = encounterId, GameSessionId = sessionId, Status = EncounterStatus.Active, RoundNumber = 1, StartedAt = DateTimeOffset.UtcNow,
            Combatants =
            [
                new EncounterCombatant { Id = scoutId, EncounterId = encounterId, UserId = "u", CharacterName = "Scout",
                    CombatantType = "player", MaxHp = 12, Speed = 12, DamageDie = "d12" },
                new EncounterCombatant { Id = goblinId, EncounterId = encounterId, UserId = "u", CharacterName = "Goblin",
                    CombatantType = "monster", IsAiControlled = true, MaxHp = 10, Speed = 10, DamageDie = "d4" },
            ],
        });
        await db.SaveChangesAsync();

        var map = new MapEntry(20, 20, new Dictionary<string, Position>
        {
            [scoutId.ToString()] = new(5, 5), [goblinId.ToString()] = new(5, 6),
        }, []);
        var plans = new Dictionary<string, string> { [scoutId.ToString()] = ActionJson("melee_attack", targetId: goblinId.ToString()) };
        // HP lives under the combatant ID (no character); the scout is hurt, the goblin is at full HP.
        var redis = BuildMockRedis(encounterId.ToString(), sessionId.ToString(), plans, map,
            hpByKey: new() { [scoutId.ToString()] = 7, [goblinId.ToString()] = 10 });

        // d20 = 15 hits, then 11 damage: only possible on the preset's d12 (ScriptedDice rejects 11 on
        // the d4 a character-less player would otherwise get).
        var service = new ResolutionService(db, redis.Object, new ScriptedDice(15, 11), NullLogger<ResolutionService>.Instance);
        var payload = await service.ResolveRoundAsync(encounterId);

        var attack = Assert.Single(payload.Ticks[0].Attacks);
        Assert.Equal((true, 11, -1), (attack.Hit, attack.Damage, attack.TargetNewHp));
        var scout = payload.UpdatedCombatants.Single(c => c.Id == scoutId.ToString());
        Assert.Equal((7, 12), (scout.CurrentHp, scout.MaxHp)); // MaxHp from the override, not the current HP
        Assert.True(payload.UpdatedCombatants.Single(c => c.Id == goblinId.ToString()).IsAiControlled);
        redis.Verify(r => r.SetHpAsync(goblinId.ToString(), -1), Times.Once);
        Assert.Equal([goblinId.ToString()], payload.DefeatedCombatantIds);
    }
}
