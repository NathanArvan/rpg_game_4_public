using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Models;
using RpgGame.Api.Services;
using RpgGame.Domain.Entities;
using RpgGame.Infrastructure.Data;
using RpgGame.Rules;

namespace RpgGame.Api.Tests;

// Characterization tests: these pin the exact observable behavior of ResolutionService
// (feed text, roll order, persistence, and known quirks) so the rules-engine extraction
// can be proven behavior-preserving. "Quirk N" refers to the numbered list in the
// refactor/extract-rules-engine PR description — current behavior, not necessarily desired.
public partial class ResolutionServiceTests
{
    // ----- Harness -----

    private sealed class Scene : IAsyncDisposable
    {
        public AppDbContext Db { get; } = CreateDb();
        public Guid SessionId { get; } = Guid.NewGuid();
        public Guid EncounterId { get; } = Guid.NewGuid();
        public Mock<IRedisGameState> Redis { get; private set; } = null!;
        public List<string> CallLog { get; } = [];

        private readonly List<EncounterCombatant> _combatants = [];
        private readonly Dictionary<string, Position> _positions = [];
        private readonly Dictionary<string, string> _plans = []; // insertion order = Redis order
        private readonly Dictionary<string, int> _hpByKey = [];
        private readonly HashSet<string> _preDefeated = [];
        public Dictionary<string, string> HpKeys { get; } = [];

        public string Monster(string name, (int x, int y)? at, int hp = 20, bool defeated = false)
        {
            var id = Guid.NewGuid();
            _combatants.Add(new EncounterCombatant
            {
                Id = id, EncounterId = EncounterId, UserId = "gm", CharacterId = null,
                CharacterName = name, CombatantType = "monster",
            });
            return Register(id.ToString(), id.ToString(), at, hp, defeated);
        }

        /// <param name="profile">Stored combat profile; null (as on pre-3B.1 rows) means the default profile.</param>
        public string Player(string name, (int x, int y)? at, CombatProfileValues? profile = null,
            int hp = 20, bool missingCharacter = false)
        {
            var id = Guid.NewGuid();
            var characterId = Guid.NewGuid();
            if (!missingCharacter)
            {
                Db.Characters.Add(new Character
                {
                    Id = characterId, UserId = $"user-{name}", Name = name, Race = "Human", Class = "fighter",
                    Level = 1, MaxHp = 20, CurrentHp = hp,
                });
            }
            _combatants.Add(new EncounterCombatant
            {
                Id = id, EncounterId = EncounterId, UserId = $"user-{name}", CharacterId = characterId,
                CharacterName = name, CombatantType = "player", Profile = profile,
            });
            return Register(id.ToString(), characterId.ToString(), at, hp, defeated: false);
        }

        private string Register(string id, string hpKey, (int x, int y)? at, int hp, bool defeated)
        {
            HpKeys[id] = hpKey;
            _hpByKey[hpKey] = hp;
            if (at is { } p) _positions[id] = new Position(p.x, p.y);
            if (defeated) _preDefeated.Add(id);
            return id;
        }

        public void Move(string id, int x, int y) => _plans[id] = ActionJson("move", new PositionDto(x, y));
        public void Melee(string id, string target) => _plans[id] = ActionJson("melee_attack", targetId: target);
        public void Ranged(string id, string target) => _plans[id] = ActionJson("ranged_attack", targetId: target);
        public void Defend(string id) => _plans[id] = ActionJson("defend");

        public async Task<ResolutionPayload> Run(ScriptedDice dice)
        {
            Db.Encounters.Add(new Encounter
            {
                Id = EncounterId, GameSessionId = SessionId, Status = EncounterStatus.Active,
                RoundNumber = 1, StartedAt = DateTimeOffset.UtcNow, Combatants = _combatants,
            });
            await Db.SaveChangesAsync();

            var map = new MapEntry(20, 20, new Dictionary<string, Position>(_positions), ["fog-a"]);
            Redis = BuildMockRedis(EncounterId.ToString(), SessionId.ToString(), _plans, map, _hpByKey, _preDefeated);
            Redis.Setup(r => r.SetHpAsync(It.IsAny<string>(), It.IsAny<long>()))
                .Callback<string, long>((k, v) => CallLog.Add($"SetHp {k}={v} events={Db.GameEvents.Count()}"))
                .Returns(Task.CompletedTask);
            Redis.Setup(r => r.MarkDefeatedAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((_, id) => CallLog.Add($"MarkDefeated {id} events={Db.GameEvents.Count()}"))
                .Returns(Task.CompletedTask);
            Redis.Setup(r => r.SetMapAsync(It.IsAny<string>(), It.IsAny<MapEntry>()))
                .Callback<string, MapEntry>((_, _) => CallLog.Add($"SetMap events={Db.GameEvents.Count()}"))
                .Returns(Task.CompletedTask);

            var service = new ResolutionService(Db, Redis.Object, dice, NullLogger<ResolutionService>.Instance);
            var result = await service.ResolveRoundAsync(EncounterId);
            Assert.Equal(0, dice.Remaining); // every scripted roll consumed — catches roll-count drift
            return result;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static AttackResult SingleAttack(ResolutionPayload r) => Assert.Single(Assert.Single(r.Ticks).Attacks);

    // ----- Hit / miss / damage feed text -----

    [Fact]
    public async Task Char_MeleeHit_AtThreshold_FeedShowsHpTransition()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var b = s.Monster("Goblin B", (1, 0));
        s.Melee(a, b);

        var r = await s.Run(new ScriptedDice(10, 5)); // d20=10 hits (>= 10), d8=5

        var atk = SingleAttack(r);
        Assert.Equal("Goblin A strikes Goblin B for 5 damage (HP: 20 → 15)", atk.FeedText);
        Assert.Equal(("Goblin A", "Goblin B", "melee_attack"), (atk.AttackerName, atk.TargetName, atk.AttackType));
        Assert.True(atk.Hit);
        Assert.Equal(5, atk.Damage);
        Assert.Equal(15, atk.TargetNewHp);
        Assert.False(atk.TargetDefeated);
        Assert.False(atk.Wasted);
        s.Redis.Verify(x => x.SetHpAsync(s.HpKeys[b], 15), Times.Once);
        s.Redis.Verify(x => x.SetHpAsync(It.IsAny<string>(), It.IsAny<long>()), Times.Once); // only changed HP written
        Assert.Equal(15, r.UpdatedCombatants.Single(c => c.Id == b).CurrentHp);
    }

    [Fact]
    public async Task Char_MeleeMiss_BelowThreshold_NoDamageRoll()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var b = s.Monster("Goblin B", (1, 0));
        s.Melee(a, b);

        var r = await s.Run(new ScriptedDice(9));

        var atk = SingleAttack(r);
        Assert.Equal("Goblin A swings at Goblin B and misses", atk.FeedText);
        Assert.False(atk.Hit);
        Assert.Equal(0, atk.Damage);
        Assert.Equal(20, atk.TargetNewHp);
        s.Redis.Verify(x => x.SetHpAsync(It.IsAny<string>(), It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task Char_MeleeHit_Lethal_FeedSaysDefeated_AndPersists()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var b = s.Monster("Goblin B", (1, 0), hp: 3);
        s.Melee(a, b);

        var r = await s.Run(new ScriptedDice(12, 8));

        var atk = SingleAttack(r);
        Assert.Equal("Goblin A strikes Goblin B for 8 damage — defeated!", atk.FeedText);
        Assert.True(atk.TargetDefeated);
        Assert.Equal(-5, atk.TargetNewHp); // HP is not clamped at 0
        Assert.Equal([b], r.DefeatedCombatantIds);
        s.Redis.Verify(x => x.SetHpAsync(s.HpKeys[b], -5), Times.Once);
        s.Redis.Verify(x => x.MarkDefeatedAsync(s.EncounterId.ToString(), b), Times.Once);
        var updated = r.UpdatedCombatants.Single(c => c.Id == b);
        Assert.True(updated.IsDefeated);
        Assert.Equal(-5, updated.CurrentHp);
    }

    [Fact]
    public async Task Char_RangedHit_UsesShootsAt_UnlimitedRange()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var b = s.Monster("Goblin B", (15, 15));
        s.Ranged(a, b);

        var r = await s.Run(new ScriptedDice(20, 4));

        var atk = SingleAttack(r);
        Assert.Equal("Goblin A shoots at Goblin B for 4 damage (HP: 20 → 16)", atk.FeedText);
        Assert.Equal("ranged_attack", atk.AttackType);
    }

    [Fact]
    public async Task Char_RangedMiss_UsesShootsAt()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var b = s.Monster("Goblin B", (10, 0));
        s.Ranged(a, b);

        var r = await s.Run(new ScriptedDice(1));

        Assert.Equal("Goblin A shoots at Goblin B and misses", SingleAttack(r).FeedText);
    }

    [Fact]
    public async Task Char_RangedHit_Lethal_UsesShootsAt()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var b = s.Monster("Goblin B", (10, 0), hp: 2);
        s.Ranged(a, b);

        var r = await s.Run(new ScriptedDice(11, 2));

        Assert.Equal("Goblin A shoots at Goblin B for 2 damage — defeated!", SingleAttack(r).FeedText);
    }

    // ----- Wasted attacks -----

    [Fact]
    public async Task Char_Quirk6_AttackOnPreviouslyDefeatedTarget_IsWasted_NoRolls()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var b = s.Monster("Goblin B", (1, 0), hp: 0, defeated: true);
        s.Melee(a, b);

        var r = await s.Run(new ScriptedDice());

        var atk = SingleAttack(r);
        Assert.Equal("Goblin A's attack on Goblin B fails — target already defeated", atk.FeedText);
        Assert.True(atk.Wasted);
        Assert.False(atk.Hit);
        Assert.Equal(0, atk.Damage);
        Assert.Equal(0, atk.TargetNewHp);
        Assert.False(atk.TargetDefeated); // not "defeated this round"
        Assert.Empty(r.DefeatedCombatantIds);
    }

    [Fact]
    public async Task Char_AttackOnUnknownTarget_IsWastedAsInvalidTarget()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var ghost = Guid.NewGuid().ToString();
        s.Melee(a, ghost);

        var r = await s.Run(new ScriptedDice());

        var atk = SingleAttack(r);
        Assert.Equal("Goblin A's attack on Unknown fails — invalid target", atk.FeedText);
        Assert.Equal("Unknown", atk.TargetName);
        Assert.Equal(ghost, atk.TargetId);
        Assert.Equal(0, atk.TargetNewHp);
        Assert.True(atk.Wasted);
    }

    [Fact]
    public async Task Char_MeleeOutOfReach_ExactFeedText()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var b = s.Monster("Goblin B", (2, 2), hp: 17);
        s.Melee(a, b);

        var r = await s.Run(new ScriptedDice());

        var atk = SingleAttack(r);
        Assert.Equal("Goblin A's attack on Goblin B fails — target out of melee range", atk.FeedText);
        Assert.Equal(17, atk.TargetNewHp);
    }

    [Fact]
    public async Task Char_MeleeReach_DiagonalAdjacencyCounts()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (5, 5));
        var b = s.Monster("Goblin B", (6, 6));
        s.Melee(a, b);

        var r = await s.Run(new ScriptedDice(10, 1));

        Assert.False(SingleAttack(r).Wasted);
    }

    // ----- Simultaneity / netting / roll order -----

    [Fact]
    public async Task Char_MutualKill_BothDefeated()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0), hp: 4);
        var b = s.Monster("Goblin B", (1, 0), hp: 4);
        s.Melee(a, b);
        s.Melee(b, a);

        var r = await s.Run(new ScriptedDice(15, 6, 15, 6));

        var attacks = Assert.Single(r.Ticks).Attacks;
        Assert.Equal(2, attacks.Count);
        Assert.Equal("Goblin A strikes Goblin B for 6 damage — defeated!", attacks[0].FeedText);
        Assert.Equal("Goblin B strikes Goblin A for 6 damage — defeated!", attacks[1].FeedText);
        Assert.Equal([a, b], r.DefeatedCombatantIds);
        s.Redis.Verify(x => x.MarkDefeatedAsync(s.EncounterId.ToString(), a), Times.Once);
        s.Redis.Verify(x => x.MarkDefeatedAsync(s.EncounterId.ToString(), b), Times.Once);
    }

    [Fact]
    public async Task Char_FocusFire_DamageNetted_HpBeforeIsTickSnapshot()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var b = s.Monster("Goblin B", (2, 0));
        var c = s.Monster("Goblin C", (1, 0));
        s.Melee(a, c);
        s.Ranged(b, c);

        var r = await s.Run(new ScriptedDice(10, 3, 10, 4));

        var attacks = Assert.Single(r.Ticks).Attacks;
        // Both lines show the pre-tick HP and the final netted HP.
        Assert.Equal("Goblin A strikes Goblin C for 3 damage (HP: 20 → 13)", attacks[0].FeedText);
        Assert.Equal("Goblin B shoots at Goblin C for 4 damage (HP: 20 → 13)", attacks[1].FeedText);
        Assert.All(attacks, x => Assert.Equal(13, x.TargetNewHp));
        s.Redis.Verify(x => x.SetHpAsync(s.HpKeys[c], 13), Times.Once);
    }

    [Fact]
    public async Task Char_FocusFire_DefeatCheckedAfterAllDeltas()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var b = s.Monster("Goblin B", (2, 0));
        var c = s.Monster("Goblin C", (1, 0), hp: 10);
        s.Melee(a, c);
        s.Melee(b, c);

        var r = await s.Run(new ScriptedDice(10, 5, 10, 5));

        var attacks = Assert.Single(r.Ticks).Attacks;
        Assert.All(attacks, x => Assert.EndsWith("for 5 damage — defeated!", x.FeedText));
        Assert.Equal([c], r.DefeatedCombatantIds);
    }

    [Fact]
    public async Task Char_RollsConsumedInPlanOrder()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var b = s.Monster("Goblin B", (2, 0));
        var c = s.Monster("Goblin C", (1, 0));
        s.Melee(b, c); // planned first → rolls first
        s.Melee(a, c);

        var r = await s.Run(new ScriptedDice(9, 10, 2));

        var attacks = Assert.Single(r.Ticks).Attacks;
        Assert.Equal(b, attacks[0].AttackerId);
        Assert.False(attacks[0].Hit);
        Assert.Equal(a, attacks[1].AttackerId);
        Assert.Equal(2, attacks[1].Damage);
    }

    // ----- Damage dice / data seeding -----

    [Fact]
    public async Task Char_PlayerDamageDieFollowsProfile()
    {
        await using var s = new Scene();
        var p = s.Player("Rogue", (0, 0), TestProfiles.Values(damage: "d6"));
        var m = s.Monster("Goblin", (1, 0));
        s.Melee(p, m);

        var r = await s.Run(new ScriptedDice(10, 6)); // d6 max
        Assert.Equal(6, SingleAttack(r).Damage);

        await using var s2 = new Scene();
        var p2 = s2.Player("Rogue", (0, 0), TestProfiles.Values(damage: "d6"));
        var m2 = s2.Monster("Goblin", (1, 0));
        s2.Melee(p2, m2);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s2.Run(new ScriptedDice(10, 7))); // 7 is not a d6 result
    }

    [Fact]
    public async Task Char_MonsterWithoutStoredProfile_DefaultsToD8_MaxHp20()
    {
        await using var s = new Scene();
        var m = s.Monster("Ogre", (0, 0), hp: 7);
        var t = s.Monster("Goblin", (1, 0));
        s.Melee(m, t);

        var r = await s.Run(new ScriptedDice(10, 8)); // d8 max

        Assert.Equal(8, SingleAttack(r).Damage);
        var ogre = r.UpdatedCombatants.Single(c => c.Id == m);
        Assert.Equal(20, ogre.MaxHp);
        Assert.Equal(7, ogre.CurrentHp);
        Assert.Equal("monster", ogre.CombatantType);
        Assert.Equal("gm", ogre.UserId);
    }

    [Fact]
    public async Task Char_PlayerWithMissingCharacter_UsesStoredProfile()
    {
        // 3B.1: combat values come only from the combatant's stored profile, so a deleted
        // Character row changes nothing (before 3B.1 this fell back to a d4 and MaxHp = current HP).
        await using var s = new Scene();
        var p = s.Player("Ghost", (0, 0), TestProfiles.Values(maxHp: 16, damage: "d4"), hp: 12, missingCharacter: true);
        var m = s.Monster("Goblin", (1, 0));
        s.Melee(p, m);

        var r = await s.Run(new ScriptedDice(10, 4));
        Assert.Equal((12, 16), (r.UpdatedCombatants.Single(c => c.Id == p).CurrentHp, r.UpdatedCombatants.Single(c => c.Id == p).MaxHp));

        await using var s2 = new Scene();
        var p2 = s2.Player("Ghost", (0, 0), TestProfiles.Values(damage: "d4"), hp: 12, missingCharacter: true);
        var m2 = s2.Monster("Goblin", (1, 0));
        s2.Melee(p2, m2);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s2.Run(new ScriptedDice(10, 5))); // 5 is not a d4 result
    }

    [Fact]
    public async Task Char_TargetDodgeAndAttackerAccuracy_FromProfiles()
    {
        // d20 7 + melee accuracy 3 = 10 misses a Dodge-11 target...
        await using var s = new Scene();
        var p = s.Player("Archer", (0, 0), TestProfiles.Values(melee: 3, ranged: 6));
        var t = s.Player("Knight", (1, 0), TestProfiles.Values(dodge: 11));
        s.Melee(p, t);
        Assert.False(SingleAttack(await s.Run(new ScriptedDice(7))).Hit);

        // ...while the same roll + ranged accuracy 6 = 13 hits it.
        await using var s2 = new Scene();
        var p2 = s2.Player("Archer", (0, 0), TestProfiles.Values(melee: 3, ranged: 6));
        var t2 = s2.Player("Knight", (1, 0), TestProfiles.Values(dodge: 11));
        s2.Ranged(p2, t2);
        Assert.True(SingleAttack(await s2.Run(new ScriptedDice(7, 5))).Hit);
    }

    // ----- Movement -----

    [Fact]
    public async Task Char_Quirk8_PathStepsDiagonallyFirst()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin", (0, 0));
        s.Move(a, 3, 1);

        var r = await s.Run(new ScriptedDice());

        var mv = Assert.Single(Assert.Single(r.Ticks).Movements);
        Assert.Equal([new(0, 0), new(1, 1), new(2, 1), new(3, 1)], mv.Path);
        Assert.Equal("Goblin", mv.CombatantName);
        Assert.Null(mv.BlockedByCombatantId);
    }

    [Fact]
    public async Task Char_BlockedByStationaryOccupant_StopsShort()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var b = s.Monster("Goblin B", (2, 0));
        s.Move(a, 4, 0);
        s.Defend(b);

        var r = await s.Run(new ScriptedDice());

        var mv = Assert.Single(Assert.Single(r.Ticks).Movements);
        Assert.True(mv.WasBlocked);
        Assert.Equal(b, mv.BlockedByCombatantId);
        Assert.Equal(new PositionDto(1, 0), mv.FinalPosition);
        Assert.Equal([new(0, 0), new(1, 0)], mv.Path);
    }

    [Fact]
    public async Task Char_Quirk9_DefeatedCombatantStillBlocksCell()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var corpse = s.Monster("Dead Goblin", (1, 0), hp: 0, defeated: true);
        s.Move(a, 2, 0);

        var r = await s.Run(new ScriptedDice());

        var mv = Assert.Single(Assert.Single(r.Ticks).Movements);
        Assert.True(mv.WasBlocked);
        Assert.Equal(corpse, mv.BlockedByCombatantId);
        Assert.Equal(new PositionDto(0, 0), mv.FinalPosition);
    }

    [Fact]
    public async Task Char_Quirk2_MoversCanSwapCells()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var b = s.Monster("Goblin B", (1, 0));
        s.Move(a, 1, 0);
        s.Move(b, 0, 0);

        var r = await s.Run(new ScriptedDice());

        var mvs = Assert.Single(r.Ticks).Movements;
        Assert.Equal(new PositionDto(1, 0), mvs.Single(m => m.CombatantId == a).FinalPosition);
        Assert.Equal(new PositionDto(0, 0), mvs.Single(m => m.CombatantId == b).FinalPosition);
        Assert.All(mvs, m => Assert.False(m.WasBlocked));
    }

    [Fact]
    public async Task Char_Quirk2_MoverPassesThroughAnotherMover()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var b = s.Monster("Goblin B", (1, 0));
        s.Move(a, 3, 0); // passes through B's starting cell while B moves away
        s.Move(b, 1, 3);

        var r = await s.Run(new ScriptedDice());

        var mvs = Assert.Single(r.Ticks).Movements;
        Assert.Equal(new PositionDto(3, 0), mvs.Single(m => m.CombatantId == a).FinalPosition);
        Assert.Equal(new PositionDto(1, 3), mvs.Single(m => m.CombatantId == b).FinalPosition);
    }

    [Fact]
    public async Task Char_ResolverStopsMovesAtRangeAndGridEdge()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var b = s.Monster("Goblin B", (17, 5));
        s.Move(a, 25, 0); // 25 cells: stops after 6
        s.Move(b, 25, 5); // off the grid: stops at x = 19

        var r = await s.Run(new ScriptedDice());

        var mvs = Assert.Single(r.Ticks).Movements;
        var mvA = mvs.Single(m => m.CombatantId == a);
        Assert.Equal(new PositionDto(6, 0), mvA.FinalPosition);
        Assert.Equal(7, mvA.Path.Count);
        Assert.False(mvA.WasBlocked);
        Assert.Equal(new PositionDto(19, 5), mvs.Single(m => m.CombatantId == b).FinalPosition);
    }

    [Fact]
    public async Task Char_MissingPosition_CannotMove()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin", at: null);
        s.Move(a, 2, 2);

        var r = await s.Run(new ScriptedDice());

        Assert.Empty(Assert.Single(r.Ticks).Movements);
        var saved = (MapEntry)s.Redis.Invocations.Single(i => i.Method.Name == nameof(IRedisGameState.SetMapAsync)).Arguments[1];
        Assert.False(saved.Positions.ContainsKey(a)); // still off the map
    }

    [Fact]
    public async Task Char_MissingPosition_CannotMelee()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", at: null);
        var b = s.Monster("Goblin B", (1, 1));
        s.Melee(a, b);

        var r = await s.Run(new ScriptedDice());

        var atk = SingleAttack(r);
        Assert.True(atk.Wasted);
        Assert.Equal("Goblin A's attack on Goblin B fails — target out of melee range", atk.FeedText);
    }

    [Fact]
    public async Task Char_ContestedCell_FasterClaimantWins_EvenIfPlannedSecond()
    {
        await using var s = new Scene();
        var monster = s.Monster("Goblin", (2, 0));                                   // default Speed 10
        var player = s.Player("Frodo", (0, 0), TestProfiles.Values(speed: 14));       // Speed 14
        s.Move(monster, 1, 0); // planned first
        s.Move(player, 1, 0);

        var r = await s.Run(new ScriptedDice());

        var mvs = Assert.Single(r.Ticks).Movements;
        var m = mvs.Single(x => x.CombatantId == monster);
        var p = mvs.Single(x => x.CombatantId == player);
        Assert.False(p.WasBlocked);
        Assert.Equal(new PositionDto(1, 0), p.FinalPosition);
        Assert.True(m.WasBlocked);
        Assert.Equal(player, m.BlockedByCombatantId);
        Assert.Equal(new PositionDto(2, 0), m.FinalPosition);
    }

    [Fact]
    public async Task Char_TiedSpeed_BlockedByFirstOtherClaimant()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var b = s.Monster("Goblin B", (2, 0));
        var c = s.Monster("Goblin C", (1, 1));
        s.Move(a, 1, 0);
        s.Move(b, 1, 0);
        s.Move(c, 1, 0);

        var r = await s.Run(new ScriptedDice());

        var mvs = Assert.Single(r.Ticks).Movements;
        Assert.All(mvs, m => Assert.True(m.WasBlocked));
        Assert.Equal(b, mvs.Single(m => m.CombatantId == a).BlockedByCombatantId);
        Assert.Equal(a, mvs.Single(m => m.CombatantId == b).BlockedByCombatantId);
        Assert.Equal(a, mvs.Single(m => m.CombatantId == c).BlockedByCombatantId);
    }

    [Fact]
    public async Task Char_MeleeReachCheckedAfterMovement_TargetStepsAway()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var b = s.Monster("Goblin B", (1, 0));
        s.Melee(a, b);
        s.Move(b, 3, 0);

        var r = await s.Run(new ScriptedDice());

        var atk = SingleAttack(r);
        Assert.True(atk.Wasted);
        Assert.Contains("out of melee range", atk.FeedText);
    }

    [Fact]
    public async Task Char_MeleeReachCheckedAfterMovement_TargetStepsIn()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var b = s.Monster("Goblin B", (4, 0));
        s.Melee(a, b);
        s.Move(b, 1, 0);

        var r = await s.Run(new ScriptedDice(10, 2));

        Assert.False(SingleAttack(r).Wasted);
    }

    [Fact]
    public async Task Char_MoveToOwnCell_ReportsStayPut()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin", (4, 4));
        s.Move(a, 4, 4);

        var r = await s.Run(new ScriptedDice());

        var tick = Assert.Single(r.Ticks);
        var mv = Assert.Single(tick.Movements);
        Assert.Equal(a, mv.CombatantId);
        Assert.Equal([new PositionDto(4, 4)], mv.Path);
        Assert.Equal(new PositionDto(4, 4), mv.FinalPosition);
        Assert.False(mv.WasBlocked);
        Assert.Empty(tick.Attacks);
        Assert.Empty(tick.Effects);
    }

    [Fact]
    public async Task Char_NoPlans_StillOneEmptyTick()
    {
        await using var s = new Scene();
        s.Monster("Goblin", (4, 4));

        var r = await s.Run(new ScriptedDice());

        var tick = Assert.Single(r.Ticks);
        Assert.Equal(0, tick.TickIndex);
        Assert.Empty(tick.Movements);
        Assert.Empty(tick.Attacks);
        Assert.Empty(tick.Effects);
        Assert.Empty(s.CallLog.Where(l => l.StartsWith("SetHp")));
    }

    // ----- Effects -----

    [Fact]
    public async Task Char_Quirk4_DefendDescription_NoMechanicalEffect()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var b = s.Monster("Goblin B", (1, 0));
        s.Melee(a, b);
        s.Defend(b);

        var r = await s.Run(new ScriptedDice(10, 5));

        var tick = Assert.Single(r.Ticks);
        var effect = Assert.Single(tick.Effects);
        Assert.Equal((b, "defend", "Goblin B holds position"), (effect.CombatantId, effect.EffectType, effect.Description));
        Assert.Equal(15, tick.Attacks.Single().TargetNewHp); // defend reduces nothing
    }

    [Fact]
    public async Task Char_DefenderDefeatedInSameTick_GetsNoEffect()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var b = s.Monster("Goblin B", (1, 0), hp: 2);
        s.Melee(a, b);
        s.Defend(b);

        var r = await s.Run(new ScriptedDice(10, 5));

        Assert.Empty(Assert.Single(r.Ticks).Effects);
    }

    [Fact]
    public async Task Char_PreDefeatedCombatant_ActionsIgnored()
    {
        await using var s = new Scene();
        var a = s.Monster("Dead", (0, 0), hp: 0, defeated: true);
        s.Move(a, 3, 3);

        var r = await s.Run(new ScriptedDice());

        Assert.Empty(Assert.Single(r.Ticks).Movements);
    }

    [Fact]
    public async Task Char_ZeroHpUnattacked_IsMarkedDefeated()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin", (0, 0), hp: 0); // HP 0 but not in the defeated set

        var r = await s.Run(new ScriptedDice());

        Assert.Equal([a], r.DefeatedCombatantIds);
        s.Redis.Verify(x => x.MarkDefeatedAsync(s.EncounterId.ToString(), a), Times.Once);
        s.Redis.Verify(x => x.SetHpAsync(It.IsAny<string>(), It.IsAny<long>()), Times.Never);
    }

    // ----- Persistence -----

    [Fact]
    public async Task Char_Persistence_EventsWrittenBeforeRedis_AndPayloadShapes()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var b = s.Monster("Goblin B", (1, 0), hp: 3);
        var c = s.Monster("Goblin C", (5, 5));
        var d = s.Monster("Goblin D", (9, 9));
        s.Melee(a, b);
        s.Move(c, 6, 5);
        s.Ranged(d, a);

        var r = await s.Run(new ScriptedDice(10, 4, 5));

        // DB GameEvents saved (3 rows) before any Redis mutation.
        Assert.Equal(
            [$"SetHp {s.HpKeys[b]}=-1 events=3", $"MarkDefeated {b} events=3", "SetMap events=3"],
            s.CallLog);

        var events = s.Db.GameEvents.ToList();
        Assert.All(events, e => Assert.Equal(s.EncounterId, e.EncounterId));
        var mvEvt = Assert.Single(events, e => e.EventType == "movement");
        Assert.Equal($"{{\"combatantId\":\"{c}\",\"finalPosition\":{{\"x\":6,\"y\":5}},\"wasBlocked\":false}}", mvEvt.Payload);
        var hitEvt = Assert.Single(events, e => e.EventType == "attack_hit");
        Assert.Equal($"{{\"attackerId\":\"{a}\",\"targetId\":\"{b}\",\"damage\":4,\"targetNewHp\":-1}}", hitEvt.Payload);
        var missEvt = Assert.Single(events, e => e.EventType == "attack_miss");
        Assert.Equal($"{{\"attackerId\":\"{d}\",\"targetId\":\"{a}\",\"damage\":0,\"targetNewHp\":20}}", missEvt.Payload);

        var map = (MapEntry)s.Redis.Invocations.Single(i => i.Method.Name == nameof(IRedisGameState.SetMapAsync)).Arguments[1];
        Assert.Equal((20, 20), (map.GridWidth, map.GridHeight));
        Assert.Equal(["fog-a"], map.FogOfWar);
        Assert.Equal(new Position(6, 5), map.Positions[c]);
        Assert.Equal(new Position(0, 0), map.Positions[a]);
        Assert.Equal(4, map.Positions.Count);

        s.Redis.Verify(x => x.ClearPlannedActionsAsync(s.EncounterId.ToString()), Times.Once);
        s.Redis.Verify(x => x.ClearLockedInAsync(s.EncounterId.ToString()), Times.Once);
        s.Redis.Verify(x => x.SetActiveEncounterAsync(s.SessionId.ToString(),
            It.Is<ActiveEncounterEntry>(e => e.RoundPhase == "between_rounds" && e.RoundNumber == 2)), Times.Once);

        Assert.Equal(2, r.NewRoundNumber);
        Assert.Equal([a, b, c, d], r.UpdatedCombatants.Select(x => x.Id));
        Assert.Equal(["Goblin A", "Goblin B", "Goblin C", "Goblin D"], r.UpdatedCombatants.Select(x => x.CharacterName));
    }

    [Fact]
    public async Task Char_Persistence_WastedAttackEventType()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var b = s.Monster("Goblin B", (5, 0));
        s.Melee(a, b);

        await s.Run(new ScriptedDice());

        var evt = Assert.Single(s.Db.GameEvents.ToList());
        Assert.Equal("attack_wasted", evt.EventType);
        Assert.Equal($"{{\"attackerId\":\"{a}\",\"targetId\":\"{b}\",\"damage\":0,\"targetNewHp\":20}}", evt.Payload);
    }

    [Fact]
    public async Task Char_ResultOrder_FollowsPlanOrder()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var b = s.Monster("Goblin B", (10, 0));
        var c = s.Monster("Goblin C", (0, 10));
        s.Move(c, 0, 12);
        s.Move(a, 0, 2);
        s.Defend(b);

        var r = await s.Run(new ScriptedDice());

        Assert.Equal([c, a], Assert.Single(r.Ticks).Movements.Select(m => m.CombatantId));
    }
}
