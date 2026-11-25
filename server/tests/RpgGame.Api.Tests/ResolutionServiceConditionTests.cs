using RpgGame.Api.Contracts.Hub;
using RpgGame.Domain.Entities;
using RpgGame.Rules;

namespace RpgGame.Api.Tests;

// Conditions (3B.4) through ResolutionService: feed text, payload fields and Redis persistence.
// Uses the characterization Scene harness; existing strings are pinned there.
public partial class ResolutionServiceTests
{
    /// <summary>A default-valued stored profile with these shipped abilities.</summary>
    private static CombatProfileValues WithAbilities(params string[] ids)
    {
        var profile = TestProfiles.Values();
        profile.Abilities = ids.Select(id => TestProfiles.Catalog.Abilities.Find(id)!.Copy()).ToList();
        return profile;
    }

    private static ActiveConditionValues Stored(string id, int roundsLeft, string? sourceId = null) => new()
    {
        Condition = TestProfiles.Catalog.Abilities.Conditions.Find(id)!.Copy(),
        RoundsLeft = roundsLeft,
        SourceId = sourceId,
    };

    [Fact]
    public async Task Mark_AppliesTwoConditions_FeedPayloadAndRedis()
    {
        await using var s = new Scene();
        var kestrel = s.Player("Kestrel", (0, 0), WithAbilities("mark"));
        var goblin = s.Monster("Goblin", (3, 0));
        s.Queue(kestrel, Ability("mark", goblin));

        var result = await s.Run(new ScriptedDice());

        var tick = Assert.Single(result.Ticks);
        Assert.Equal(
            ["Kestrel's Mark: Goblin is Marked until the end of next round",
             "Kestrel's Mark: Goblin is Exposed until the end of next round"],
            tick.Conditions!.Select(c => c.FeedText));
        Assert.All(tick.Conditions!, c => Assert.Equal(("mark", "applied", 1, true), (c.AbilityId, c.Result, c.RoundsLeft, c.Harmful)));

        // After the end of round they count down to 0: they last through the next round.
        var write = Assert.Single(s.ConditionWrites);
        Assert.Equal(goblin, write.Id);
        Assert.Equal([("marked", 0, kestrel), ("exposed", 0, kestrel)],
            write.Conditions.Select(c => (c.Condition.Id, c.RoundsLeft, c.SourceId)));
        Assert.Equal("Takes 2 extra damage from every hit.", write.Conditions[0].Condition.Description);

        var next = result.UpdatedCombatants.Single(c => c.Id == goblin);
        Assert.Equal(["marked", "exposed"], next.Conditions!.Select(c => c.Condition.Id));
        var modifiers = next.Conditions![0].Condition.Modifiers;
        Assert.Equal((0, 0, "any", 0, 0, 0), (modifiers.Accuracy, modifiers.Dodge, modifiers.AttackKind, modifiers.DamageDealt, modifiers.Movement, modifiers.Ap));
        Assert.Equal((0, 2), (modifiers.DamageTaken!.Percent, modifiers.DamageTaken.Flat));
        Assert.Empty(modifiers.DamageTaken.Tags);
        Assert.Null(modifiers.Periodic);
        Assert.Empty(modifiers.Blocks);
        Assert.Empty(result.ExpiredConditions!);

        // The ability's own DTO says what it applies (tooltips).
        var mark = Assert.Single(result.UpdatedCombatants.Single(c => c.Id == kestrel).Abilities!);
        Assert.Equal([("marked", 1, false), ("exposed", 1, false)],
            mark.AppliesConditions!.Select(c => (c.Condition.Id, c.Duration, c.RequiresHit)));
    }

    [Fact]
    public async Task StoredConditions_ChangeTheHit_AndTheFeedShowsHowMuch()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        var b = s.Monster("Goblin B", (1, 0));
        s.Conditions[b] = [Stored("fortified", 0)];
        s.Melee(a, b);

        var result = await s.Run(new ScriptedDice(15, 7));

        var attack = SingleAttack(result);
        Assert.Equal("Goblin A strikes Goblin B for 3 damage (-4 from conditions) (HP: 20 → 17)", attack.FeedText);
        Assert.Equal((3, -4), (attack.Damage, attack.ConditionDamage));

        // Fortified (0 rounds left) runs out at the end of the round: the entry is removed.
        var write = Assert.Single(s.ConditionWrites);
        Assert.Equal((b, 0), (write.Id, write.Conditions.Count));
        var expired = Assert.Single(result.ExpiredConditions!);
        Assert.Equal(("fortified", "Fortified", "Goblin B is no longer Fortified"), (expired.ConditionId, expired.ConditionName, expired.FeedText));
    }

    [Fact]
    public async Task AbilityAttack_WithConditions_FeedShowsMoveAndConditionBonuses()
    {
        await using var s = new Scene();
        var skirmisher = s.Player("Skirmisher", (5, 9), TestProfiles.Catalog.Find("skirmisher")!.Snapshot());
        var goblin = s.Monster("Goblin", (5, 5));
        s.Conditions[goblin] = [Stored("marked", 1)];
        s.Queue(skirmisher, Ability("charge", goblin));

        // Skirmisher: melee +1, d6. d6 4 + 3 for moving + 2 Marked = 9.
        var result = await s.Run(new ScriptedDice(10, 4));

        Assert.Equal("Skirmisher's Charge hits Goblin for 9 damage (+3 from moving) (+2 from conditions) (HP: 20 → 11)",
            SingleAttack(result).FeedText);
        // Marked counts down from 1 to 0 and is written back.
        Assert.Equal(0, Assert.Single(Assert.Single(s.ConditionWrites).Conditions).RoundsLeft);
    }

    [Fact]
    public async Task UnchangedConditions_AreNotRewritten()
    {
        await using var s = new Scene();
        var a = s.Monster("Goblin A", (0, 0));
        s.Monster("Goblin B", (9, 9));
        // Nothing stored, nothing applied: no writes at all.
        s.Move(a, 1, 0);

        await s.Run(new ScriptedDice());

        Assert.Empty(s.ConditionWrites);
    }

    [Fact]
    public async Task RefreshedCondition_FeedSaysStill()
    {
        await using var s = new Scene();
        var kestrel = s.Player("Kestrel", (0, 0), WithAbilities("mark"));
        var goblin = s.Monster("Goblin", (3, 0));
        s.Conditions[goblin] = [Stored("marked", 2)];
        s.Queue(kestrel, Ability("mark", goblin));

        var result = await s.Run(new ScriptedDice());

        var first = Assert.Single(result.Ticks).Conditions![0];
        Assert.Equal(("refreshed", 2), (first.Result, first.RoundsLeft));
        Assert.Equal("Kestrel's Mark: Goblin is still Marked, now for 2 more rounds", first.FeedText);
    }

    [Fact]
    public async Task ConditionOnDownedTarget_IsWasted_WithAFeedLine()
    {
        await using var s = new Scene();
        var hero = s.Player("Hero", (0, 0));
        s.StartDowned(hero, 2);
        var shaman = s.Monster("Shaman", (0, 5), profile: WithAbilities("mark"));
        s.Queue(shaman, Ability("mark", hero));

        var result = await s.Run(new ScriptedDice());

        var wasted = Assert.Single(result.Ticks).Conditions!.First();
        Assert.Equal(("wasted", "target_defeated"), (wasted.Result, wasted.WastedReason));
        Assert.Equal("Shaman's Mark on Hero fails — target already defeated", wasted.FeedText);
        Assert.Contains(s.Db.GameEvents, e => e.EventType == "condition_wasted");
    }

    // ----- Movement, AP and action denial (3B.4 PR 3) -----

    [Fact]
    public async Task ShieldBash_StunsForTheRest_OfTheRound_AndTheBlockedActionIsReported()
    {
        await using var s = new Scene();
        var brakka = s.Player("Brakka", (0, 0), WithAbilities("shield_bash"));
        var goblin = s.Monster("Goblin", (1, 0));
        s.Queue(brakka, Ability("shield_bash", goblin));
        s.Queue(goblin, new PlannedActionEntry("defend", null, null), new PlannedActionEntry("melee_attack", null, brakka));

        // Guarded makes the Goblin's Dodge 13: the bash needs 13+.
        var result = await s.Run(new ScriptedDice(15, 2));

        Assert.Equal("Brakka's Shield Bash: Goblin is Stunned until the end of the round",
            result.Ticks[0].Conditions!.Single(c => c.ConditionId == "stunned").FeedText);
        var blocked = Assert.Single(result.Ticks[1].Blocked!);
        Assert.Equal((goblin, "melee_attack", "stunned", "Stunned", "all", "Goblin is Stunned and can't act"),
            (blocked.CombatantId, blocked.ActionType, blocked.ConditionId, blocked.ConditionName, blocked.Block, blocked.FeedText));
        Assert.Empty(result.Ticks[1].Attacks);
        Assert.Contains(s.Db.GameEvents, e => e.EventType == "action_blocked");
        Assert.Contains("Goblin is no longer Stunned", result.ExpiredConditions!.Select(x => x.FeedText));
    }

    [Fact]
    public async Task RootedMove_IsBlocked_AndReported()
    {
        await using var s = new Scene();
        var goblin = s.Monster("Goblin", (1, 0));
        s.Conditions[goblin] = [Stored("rooted", 0)];
        s.Move(goblin, 4, 0);

        var result = await s.Run(new ScriptedDice());

        var blocked = Assert.Single(result.Ticks[0].Blocked!);
        Assert.Equal(("move", "move", "Goblin is Rooted and can't move"), (blocked.ActionType, blocked.Block, blocked.FeedText));
        Assert.Empty(result.Ticks[0].Movements);
    }

    [Fact]
    public async Task NextPlanningPhase_CarriesEffectiveBudgets_BaseValues_AndBlocks()
    {
        await using var s = new Scene();
        var hero = s.Player("Hero", (0, 0), WithAbilities("mark"));
        var goblin = s.Monster("Goblin", (5, 0));
        s.Conditions[hero] = [Stored("hasted", 1)];
        s.Conditions[goblin] = [Stored("slowed", 1), Stored("weakened", 1), Stored("rooted", 1), Stored("stunned", 1)];

        var result = await s.Run(new ScriptedDice());

        var h = result.UpdatedCombatants.Single(c => c.Id == hero);
        Assert.Equal((h.BaseMovementRange + 2, h.BaseMaxAp + 1), (h.MovementRange, h.MaxAp));
        Assert.Empty(h.Blocks!);
        var g = result.UpdatedCombatants.Single(c => c.Id == goblin);
        Assert.Equal((g.BaseMovementRange - 2, g.BaseMaxAp - 1), (g.MovementRange, g.MaxAp));
        Assert.Equal([("all", "stunned", "Stunned"), ("move", "stunned", "Stunned")],
            g.Blocks!.Select(b => (b.Kind, b.ConditionId, b.ConditionName)));
    }
}
