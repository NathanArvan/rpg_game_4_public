using Moq;
using RpgGame.Api.Models;
using RpgGame.Rules;

namespace RpgGame.Api.Tests;

// Periodic effects and ConditionResist (3B.4 PR 2) through ResolutionService: feed text, payload fields,
// GameEvents and Redis persistence of an end-of-round fall.
public partial class ResolutionServiceTests
{
    [Fact]
    public async Task Firebolt_SetsBurning_WhichBurnsAtTheEndOfTheRound()
    {
        await using var s = new Scene();
        var kestrel = s.Player("Kestrel", (0, 0), WithAbilities("firebolt"));
        var goblin = s.Monster("Goblin", (5, 0));
        s.Queue(kestrel, Ability("firebolt", goblin));

        // d20 15 hits; d8 5 + 1 = 6; Burning d4 3 at the end of the round.
        var result = await s.Run(new ScriptedDice(15, 5, 3));

        var burn = Assert.Single(Assert.Single(result.Ticks).Conditions!);
        Assert.Equal("Kestrel's Firebolt: Goblin is Burning until the end of next round", burn.FeedText);

        var periodic = Assert.Single(result.Periodic!);
        Assert.Equal((goblin, "Goblin", "burning", "Burning", true, "damage", 3, 0, 11, "standing", false),
            (periodic.CombatantId, periodic.CombatantName, periodic.ConditionId, periodic.ConditionName, periodic.Harmful,
             periodic.Kind, periodic.Amount, periodic.ConditionDamage, periodic.NewHp, periodic.Status, periodic.Fell));
        Assert.Equal(["flame"], periodic.Tags);
        Assert.Equal("Goblin takes 3 damage from Burning (HP: 14 → 11)", periodic.FeedText);

        // The burn is part of the round's HP: one write with the final value.
        s.Redis.Verify(x => x.SetHpAsync(s.HpKeys[goblin], 11), Times.Once);
        Assert.Equal(11, result.UpdatedCombatants.Single(c => c.Id == goblin).CurrentHp);
        Assert.Equal(("burning", 0), (Assert.Single(Assert.Single(s.ConditionWrites).Conditions).Condition.Id, 0));
        var evt = Assert.Single(s.Db.GameEvents, e => e.EventType == "periodic_damage");
        Assert.Equal($"{{\"combatantId\":\"{goblin}\",\"conditionId\":\"burning\",\"amount\":3,\"newHp\":11}}", evt.Payload);
    }

    [Fact]
    public async Task Burning_KillsAMonster_AtTheEndOfTheRound()
    {
        await using var s = new Scene();
        var goblin = s.Monster("Goblin", (5, 0), hp: 2);
        s.Player("Hero", (9, 9));
        s.Conditions[goblin] = [Stored("burning", 1)];

        var result = await s.Run(new ScriptedDice(4));

        Assert.Equal([goblin], result.DefeatedCombatantIds);
        var periodic = Assert.Single(result.Periodic!);
        Assert.Equal(("dead", true), (periodic.Status, periodic.Fell));
        Assert.Equal("Goblin takes 4 damage from Burning — defeated!", periodic.FeedText);
        s.Redis.Verify(x => x.MarkDefeatedAsync(s.EncounterId.ToString(), goblin), Times.Once);
        s.Redis.Verify(x => x.SetStatusAsync(s.EncounterId.ToString(), goblin, new CombatantStatusEntry("dead", 0)), Times.Once);
        // Falling clears conditions: nothing expires, and the stored list is emptied.
        Assert.Empty(result.ExpiredConditions!);
        Assert.Empty(Assert.Single(s.ConditionWrites).Conditions);
        // The last enemy burned down after the last tick: the encounter still ends.
        Assert.Equal("party", RpgGame.Api.Services.EncounterEndCheck.Evaluate(result.UpdatedCombatants));
    }

    [Fact]
    public async Task Burning_DownsAHero_WhoDoesNotBleedThisRound()
    {
        await using var s = new Scene();
        var hero = s.Player("Hero", (0, 0), hp: 2);
        s.Monster("Goblin", (9, 9));
        s.Conditions[hero] = [Stored("burning", 1)];

        var result = await s.Run(new ScriptedDice(3));

        Assert.Equal("Hero takes 3 damage from Burning — goes down!", Assert.Single(result.Periodic!).FeedText);
        Assert.Empty(result.EndOfRound);
        s.Redis.Verify(x => x.SetStatusAsync(s.EncounterId.ToString(), hero, new CombatantStatusEntry("downed", 2)), Times.Once);
    }

    [Fact]
    public async Task PoisonAndRegeneration_NetTogether_OnlyTheLastLineShowsHp()
    {
        await using var s = new Scene();
        var mira = s.Player("Mira", (0, 0), hp: 5);
        s.Conditions[mira] = [Stored("poisoned", 1), Stored("regenerating", 1)];

        var result = await s.Run(new ScriptedDice(4)); // Regenerating d4 4; Poisoned is a flat 2

        Assert.Equal(["Mira takes 2 damage from Poisoned", "Mira heals 4 from Regenerating (HP: 5 → 7)"],
            result.Periodic!.Select(p => p.FeedText));
        Assert.All(result.Periodic!, p => Assert.Equal(7, p.NewHp));
        Assert.Contains(s.Db.GameEvents, e => e.EventType == "periodic_heal");
    }

    [Fact]
    public async Task ConditionResist_RollsD100_AndTheFeedSaysResisted()
    {
        await using var s = new Scene();
        var kestrel = s.Player("Kestrel", (0, 0), WithAbilities("mark"));
        var stubborn = TestProfiles.Values();
        stubborn.ConditionResist = 25;
        var ogre = s.Monster("Ogre", (3, 0), profile: stubborn);
        s.Queue(kestrel, Ability("mark", ogre));

        var result = await s.Run(new ScriptedDice(25, 90)); // Marked: 25 ≤ 25 resisted; Exposed: 90 lands

        var conditions = Assert.Single(result.Ticks).Conditions!;
        Assert.Equal(("resisted", 0, "Ogre resists Marked"), (conditions[0].Result, conditions[0].RoundsLeft, conditions[0].FeedText));
        Assert.Equal("applied", conditions[1].Result);
        Assert.Contains(s.Db.GameEvents, e => e.EventType == "condition_resisted");
        Assert.Equal(["exposed"], Assert.Single(s.ConditionWrites).Conditions.Select(c => c.Condition.Id));
    }
}
