using Moq;
using RpgGame.Api.Contracts.Hub;
using RpgGame.Api.Models;
using RpgGame.Api.Services;
using RpgGame.Rules;

namespace RpgGame.Api.Tests;

// 3B.6 PR 1: opportunity attacks and Disengage through the Api adapter. Pins the new feed lines, the reaction and
// movement fields on the wire, the GameEvent and persistence of a mover dropped mid-path. Earlier feed strings are
// pinned (unchanged) by the characterization tests.
public partial class ResolutionServiceTests
{
    private static PlannedActionEntry Disengage(int x, int y) => new("disengage", new PositionDto(x, y), null);

    [Fact]
    public async Task Reaction_OpportunityAttackHit_FeedAndWireFields()
    {
        await using var s = new Scene();
        var hero = s.Player("Mira", (5, 5));
        var goblin = s.Monster("Goblin", (6, 5));
        s.Move(hero, 2, 5);

        var r = await s.Run(new ScriptedDice(12, 4));

        var tick = Assert.Single(r.Ticks);
        var reaction = Assert.Single(tick.Reactions!);
        Assert.Equal("Goblin's opportunity attack on Mira hits for 4 damage (HP: 20 → 16)", reaction.FeedText);
        Assert.Equal((goblin, "Goblin", hero, "Mira", "opportunity", 1), (reaction.ReactorId, reaction.ReactorName,
            reaction.TriggerId, reaction.TriggerName, reaction.Kind, reaction.Step));
        Assert.Null(reaction.AbilityId);
        var attack = reaction.Attack!;
        Assert.Equal(("melee_attack", true, 4, 16), (attack.AttackType, attack.Hit, attack.Damage, attack.TargetNewHp));
        Assert.Equal(reaction.FeedText, attack.FeedText);
        Assert.Equal(new AttackRollDto(12, 0, 10), attack.Roll);
        Assert.Empty(tick.Attacks);
        Assert.Empty(tick.Watching!);
        Assert.Empty(r.UnusedReactions!);

        var move = Assert.Single(tick.Movements);
        Assert.Equal(("none", false), (move.StopReason, move.Disengaging));
        Assert.Equal(new PositionDto(2, 5), move.FinalPosition);
        Assert.Equal(16, r.UpdatedCombatants.Single(c => c.Id == hero).CurrentHp);
        Assert.Single(s.Db.GameEvents.Where(e => e.EventType == "opportunity_attack"));
    }

    [Fact]
    public async Task Reaction_OpportunityAttackMiss_Feed()
    {
        await using var s = new Scene();
        var hero = s.Player("Mira", (5, 5));
        s.Monster("Goblin", (6, 5));
        s.Move(hero, 2, 5);

        var r = await s.Run(new ScriptedDice(9));

        Assert.Equal("Goblin's opportunity attack on Mira misses", Assert.Single(r.Ticks[0].Reactions!).FeedText);
    }

    [Fact]
    public async Task Reaction_HeroDownedMidPath_StopsThere_AndIsPersistedAsDowned()
    {
        await using var s = new Scene();
        var hero = s.Player("Mira", (5, 5), hp: 3);
        s.Monster("Goblin", (6, 5));
        s.Move(hero, 1, 5);

        var r = await s.Run(new ScriptedDice(12, 5));

        var tick = Assert.Single(r.Ticks);
        Assert.Equal("Goblin's opportunity attack on Mira hits for 5 damage — goes down!", Assert.Single(tick.Reactions!).FeedText);
        var move = Assert.Single(tick.Movements);
        Assert.Equal(("fell", false, (string?)null), (move.StopReason, move.WasBlocked, move.BlockedByCombatantId));
        Assert.Equal(new PositionDto(4, 5), move.FinalPosition);
        Assert.Equal([hero], r.DefeatedCombatantIds);
        s.Redis.Verify(x => x.SetStatusAsync(s.EncounterId.ToString(), hero, new CombatantStatusEntry("downed", RulesConfig.Default.BleedoutRounds)), Times.Once);
        s.Redis.Verify(x => x.MarkDefeatedAsync(s.EncounterId.ToString(), hero), Times.Once);
        s.Redis.Verify(x => x.SetMapAsync(s.EncounterId.ToString(), It.Is<MapEntry>(m => m.Positions[hero] == new Position(4, 5))), Times.Once);
    }

    [Fact]
    public async Task Reaction_MonsterKilledMidPath_FeedSaysDefeated()
    {
        await using var s = new Scene();
        s.Player("Mira", (5, 5));
        var goblin = s.Monster("Goblin", (6, 5), hp: 2);
        s.Move(goblin, 9, 5);

        var r = await s.Run(new ScriptedDice(15, 6));

        Assert.Equal("Mira's opportunity attack on Goblin hits for 6 damage — defeated!", Assert.Single(r.Ticks[0].Reactions!).FeedText);
        Assert.Equal([goblin], r.DefeatedCombatantIds);
    }

    [Fact]
    public async Task Disengage_ProvokesNothing_CostsTwoAp_AndIsFlaggedOnTheMove()
    {
        await using var s = new Scene();
        var hero = s.Player("Mira", (5, 5));
        s.Monster("Goblin", (6, 5));
        s.Queue(hero, Disengage(2, 5));

        var r = await s.Run(new ScriptedDice());

        var tick = Assert.Single(r.Ticks);
        Assert.Empty(tick.Reactions!);
        Assert.True(Assert.Single(tick.Movements).Disengaging);
        var queued = Assert.Single(Assert.Single(r.Queues, q => q.CombatantId == hero).Actions);
        Assert.Equal(("disengage", 2), (queued.Type, queued.ApCost));
    }

    [Fact]
    public async Task Reaction_ProfileWithoutOpportunityAttacks_DoesntReact()
    {
        await using var s = new Scene();
        var hero = s.Player("Mira", (5, 5));
        var profile = TestProfiles.Values();
        profile.OpportunityAttacks = false;
        s.Monster("Dummy", (6, 5), profile: profile);
        s.Move(hero, 2, 5);

        var r = await s.Run(new ScriptedDice());

        Assert.Empty(r.Ticks[0].Reactions!);
    }
}
