using RpgGame.Rules;

namespace RpgGame.Rules.Tests;

/// <summary>Conditions (3B.4): <see cref="ConditionMath"/> and the resolver cases scenarios can't express well.</summary>
public class ConditionTests
{
    private static ActiveCondition Active(Modifiers modifiers, string id = "c", int rounds = 0) =>
        new(new ConditionDefinition(id, id, Harmful: false, Tags: [], modifiers), rounds);

    // ----- Hit check -----

    [Fact]
    public void Accuracy_IsSummedAcrossConditions()
    {
        var list = new[] { Active(new Modifiers(Accuracy: 2), "blessed"), Active(new Modifiers(Accuracy: -3), "dazzled") };
        Assert.Equal(-1, ConditionMath.AccuracyBonus(list, AttackKind.Melee));
    }

    [Theory]
    [InlineData(AttackKindFilter.Any, AttackKind.Melee, 2)]
    [InlineData(AttackKindFilter.Any, AttackKind.Ranged, 2)]
    [InlineData(AttackKindFilter.Melee, AttackKind.Melee, 2)]
    [InlineData(AttackKindFilter.Melee, AttackKind.Ranged, 0)]
    [InlineData(AttackKindFilter.Ranged, AttackKind.Melee, 0)]
    [InlineData(AttackKindFilter.Ranged, AttackKind.Ranged, 2)]
    [InlineData(AttackKindFilter.Any, AttackKind.None, 0)]
    public void AccuracyAndDodge_FollowTheAttackKindFilter(AttackKindFilter filter, AttackKind kind, int expected)
    {
        var list = new[] { Active(new Modifiers(Accuracy: 2, Dodge: 2, AttackKind: filter)) };
        Assert.Equal(expected, ConditionMath.AccuracyBonus(list, kind));
        Assert.Equal(expected, ConditionMath.DodgeBonus(list, kind));
    }

    // ----- Damage taken -----

    [Theory]
    [InlineData(7, -50, 0, 3)]    // halved, rounded down
    [InlineData(5, 50, 0, 7)]     // 7.5 → 7
    [InlineData(5, 0, 2, 7)]      // flat
    [InlineData(5, 0, -8, 0)]     // never below 0
    [InlineData(6, -50, 2, 4)]    // flat first, then percent: (6 + 2) × 0.5
    [InlineData(9, -100, 0, 0)]   // immune
    public void DamageTaken_FlatThenPercent_RoundedDown(int damage, int percent, int flat, int expected)
    {
        var list = new[] { Active(new Modifiers(DamageTaken: new DamageTakenModifier(percent, flat))) };
        Assert.Equal(expected, ConditionMath.ApplyDamageTaken(damage, list, ["melee"]));
    }

    [Fact]
    public void DamageTaken_PercentsAreSummedAndClamped()
    {
        var list = new[]
        {
            Active(new Modifiers(DamageTaken: new DamageTakenModifier(-80)), "a"),
            Active(new Modifiers(DamageTaken: new DamageTakenModifier(-80)), "b"),
        };
        Assert.Equal(0, ConditionMath.ApplyDamageTaken(10, list, []));
    }

    [Fact]
    public void DamageTaken_WithTags_OnlyCountsMatchingSources()
    {
        var list = new[] { Active(new Modifiers(DamageTaken: new DamageTakenModifier(-50, Tags: ["flame"]))) };
        Assert.Equal(8, ConditionMath.ApplyDamageTaken(8, list, ["melee"]));
        Assert.Equal(4, ConditionMath.ApplyDamageTaken(8, list, ["flame", "ranged", "spell"]));
    }

    [Fact]
    public void AttackTags_AreTheAbilitysOrMeleeRanged()
    {
        Assert.Equal(["melee"], ConditionMath.AttackTags(null, AttackKind.Melee));
        Assert.Equal(["ranged"], ConditionMath.AttackTags(null, AttackKind.Ranged));
        var bolt = new AbilityDefinition("bolt", "Bolt", 2, 0, ["flame", "spell"],
            new Targeting(TargetKind.Combatant, TargetFilter.Enemy, 8), []);
        Assert.Equal(["flame", "spell"], ConditionMath.AttackTags(bolt, AttackKind.Ranged));
    }

    // ----- Applying and counting down -----

    [Fact]
    public void Apply_SameCondition_RefreshesToTheLongerDuration()
    {
        var marked = Active(new Modifiers(), "marked", rounds: 2);
        var list = ConditionMath.Apply([marked], marked with { RoundsLeft = 1, SourceId = "new" }, out bool refreshed);
        Assert.True(refreshed);
        var only = Assert.Single(list);
        Assert.Equal((2, "new"), (only.RoundsLeft, only.SourceId));
    }

    [Fact]
    public void Apply_NewCondition_IsAddedAtTheEnd()
    {
        var list = ConditionMath.Apply([Active(new Modifiers(), "a")], Active(new Modifiers(), "b", 1), out bool refreshed);
        Assert.False(refreshed);
        Assert.Equal(["a", "b"], list.Select(c => c.Id));
    }

    [Fact]
    public void CountDown_RemovesZerosAndDecrementsTheRest()
    {
        var list = ConditionMath.CountDown(
            [Active(new Modifiers(), "gone", 0), Active(new Modifiers(), "stays", 2)], out var expired);
        Assert.Equal(["gone"], expired);
        var stays = Assert.Single(list);
        Assert.Equal(("stays", 1), (stays.Id, stays.RoundsLeft));
    }

    // ----- Resolver -----

    private static CombatantState Fighter(string id, int x, int y, int hp = 20, bool downs = false,
        CombatantStatus status = CombatantStatus.Standing, params ActiveCondition[] conditions) =>
        new(id, id, hp, CombatProfile.Default with { CanBeDowned = downs }, new GridPosition(x, y), status,
            status == CombatantStatus.Downed ? 2 : 0) { Conditions = conditions };

    [Fact]
    public void FinishingBlow_IgnoresConditions()
    {
        // A Downed target is finished off by any hit; Fortified doesn't save it and Guarded's Dodge
        // would apply to the hit check only if it were standing (conditions are cleared on falling).
        var fortified = Active(new Modifiers(DamageTaken: new DamageTakenModifier(-100)), "fortified", 2);
        var state = new EncounterState(20, 20,
        [
            Fighter("A", 0, 0),
            Fighter("B", 1, 0, hp: 0, downs: true, status: CombatantStatus.Downed, fortified),
        ]);
        var plans = new Dictionary<string, IReadOnlyList<PlannedAction>> { ["A"] = [PlannedAction.Melee("B")] };

        var o = RoundResolver.Resolve(state, plans, new ScriptedDice(10));

        Assert.True(o.Ticks[0].Attacks[0].FinishingBlow);
        Assert.Equal(CombatantStatus.Dead, o.Next.Combatants[1].Status);
        Assert.Empty(o.Next.Combatants[1].Conditions);
    }

    [Fact]
    public void AttackRoll_ReportsTheConditionParts()
    {
        var blessed = Active(new Modifiers(Accuracy: 2), "blessed");
        var exposed = Active(new Modifiers(Dodge: -3), "exposed");
        var state = new EncounterState(20, 20, [Fighter("A", 0, 0, conditions: blessed), Fighter("B", 1, 0, conditions: exposed)]);
        var plans = new Dictionary<string, IReadOnlyList<PlannedAction>> { ["A"] = [PlannedAction.Melee("B")] };

        var o = RoundResolver.Resolve(state, plans, new ScriptedDice(5, 4));

        var roll = o.Ticks[0].Attacks[0].Roll!;
        Assert.Equal((5, 2, 7, 2, -3), (roll.D20, roll.Accuracy, roll.TargetDodge, roll.ConditionAccuracy, roll.ConditionDodge));
        Assert.True(o.Ticks[0].Attacks[0].Hit);
    }

    [Fact]
    public void ResolverDoesNotMutateInputConditions()
    {
        var blessed = Active(new Modifiers(Accuracy: 2), "blessed", 1);
        var a = Fighter("A", 0, 0, conditions: blessed);
        var state = new EncounterState(20, 20, [a, Fighter("B", 5, 5)]);
        var plans = new Dictionary<string, IReadOnlyList<PlannedAction>> { ["A"] = [PlannedAction.Defend()] };

        var o = RoundResolver.Resolve(state, plans, new ScriptedDice());

        Assert.Equal([blessed], a.Conditions);
        Assert.Equal([blessed with { RoundsLeft = 0 }], o.Next.Combatants[0].Conditions); // Guarded expired, Blessed counted down
    }

    // ----- Periodic effects (PR 2) -----

    private static ActiveCondition Burning(int rounds = 0) =>
        new(new ConditionDefinition("burning", "Burning", Harmful: true, Tags: ["flame"],
            new Modifiers(Periodic: new PeriodicEffect(PeriodicKind.Damage, DamageDie.D4))), rounds);

    [Fact]
    public void EndOfRoundFall_IsReportedInDefeatedThisRound_WithBothSidesFallingTogether()
    {
        // Two combatants burn down at the same end of round: both fall, like a mutual kill in a tick.
        var state = new EncounterState(20, 20, [Fighter("A", 0, 0, hp: 2, downs: true, conditions: Burning()), Fighter("B", 5, 5, hp: 1, conditions: Burning())]);

        var o = RoundResolver.Resolve(state, new Dictionary<string, IReadOnlyList<PlannedAction>>(), new ScriptedDice(3, 1));

        Assert.Equal(["A", "B"], o.DefeatedThisRound);
        Assert.Equal([CombatantStatus.Downed, CombatantStatus.Dead], o.Next.Combatants.Select(c => c.Status));
        Assert.Equal(2, o.Next.Combatants[0].BleedoutRoundsLeft); // fell this round: no countdown yet
        Assert.Empty(o.EndOfRound);
        Assert.Empty(o.ExpiredConditions); // cleared by falling, not expired
        var a = o.PeriodicEffects[0];
        Assert.Equal(("A", "burning", PeriodicKind.Damage, 3, 2, 0, CombatantStatus.Downed, true),
            (a.CombatantId, a.ConditionId, a.Kind, a.Amount, a.HpBefore, a.HpAfter, a.Status, a.Fell));
    }

    [Fact]
    public void Periodic_DoesNotTouchSomeoneAlreadyDown()
    {
        // Input that (wrongly) carries a condition on a Downed combatant: it's ignored (C10), so nothing rolls.
        var state = new EncounterState(20, 20, [Fighter("A", 0, 0, hp: 0, downs: true, status: CombatantStatus.Downed, Burning(1))]);

        var o = RoundResolver.Resolve(state, new Dictionary<string, IReadOnlyList<PlannedAction>>(), new ScriptedDice());

        Assert.Empty(o.PeriodicEffects);
    }

    [Fact]
    public void ConditionResist_IsPartOfProfileEquality()
    {
        Assert.NotEqual(CombatProfile.Default, CombatProfile.Default with { ConditionResist = 25 });
        Assert.Equal(CombatProfile.Default with { ConditionResist = 25 }, CombatProfile.Default with { ConditionResist = 25 });
    }
}
