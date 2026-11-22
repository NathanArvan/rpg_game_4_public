using RpgGame.Rules;

namespace RpgGame.Rules.Tests;

/// <summary>Abilities (3B.3): plan checks, relations, costs and the damage spec.</summary>
public class AbilityTests
{
    private static readonly AbilityDefinition PowerStrike = new(
        "power_strike", "Power Strike", ApCost: 3, Cooldown: 2, Tags: ["melee"],
        new Targeting(TargetKind.Combatant, TargetFilter.Enemy, Range: 1),
        [new AttackEffect(AttackKind.Melee, new DamageSpec(Bonus: 4))]);

    private static readonly AbilityDefinition Jab = new(
        "jab", "Jab", ApCost: 1, Cooldown: 0, Tags: ["melee"],
        new Targeting(TargetKind.Combatant, TargetFilter.Enemy, Range: 1),
        [new AttackEffect(AttackKind.Melee, DamageSpec.Weapon)]);

    private static CombatProfile Profile(int ap = 5, params AbilityDefinition[] abilities) =>
        (CombatProfile.Default with { MaxAp = ap }) with { Abilities = abilities };

    private static PlanCheck Check(CombatProfile profile, IReadOnlyDictionary<string, int>? cooldowns, params PlannedAction[] queue) =>
        PlanValidator.Validate(new GridPosition(0, 0), profile, queue, 20, 20, cooldowns: cooldowns);

    // ----- Plan checks -----

    [Fact]
    public void AbilityCost_ComesFromItsDefinition()
    {
        var check = Check(Profile(5, PowerStrike), null, PlannedAction.Ability("power_strike", "B"), PlannedAction.Move(1, 0));
        Assert.True(check.IsValid, check.Error);
        Assert.Equal(4, check.ApSpent);
    }

    [Fact]
    public void AbilityOverBudget_IsRejected()
    {
        var check = Check(Profile(3, PowerStrike), null, PlannedAction.Ability("power_strike", "B"), PlannedAction.Defend());
        Assert.Contains("Not enough AP", check.Error);
    }

    [Fact]
    public void AbilityNotInLoadout_IsRejected()
    {
        var check = Check(Profile(5), null, PlannedAction.Ability("power_strike", "B"));
        Assert.Equal("This combatant doesn't have the ability 'power_strike'.", check.Error);
    }

    [Theory]
    [InlineData(1, "Power Strike is on cooldown for 1 more round.")]
    [InlineData(2, "Power Strike is on cooldown for 2 more rounds.")]
    public void AbilityOnCooldown_IsRejected(int left, string expected)
    {
        var check = Check(Profile(5, PowerStrike), new Dictionary<string, int> { ["power_strike"] = left },
            PlannedAction.Ability("power_strike", "B"));
        Assert.Equal(expected, check.Error);
    }

    [Fact]
    public void AbilityWithCooldown_OnlyOncePerQueue()
    {
        var check = Check(Profile(10, PowerStrike), null,
            PlannedAction.Ability("power_strike", "B"), PlannedAction.Ability("power_strike", "B"));
        Assert.Equal("Power Strike can only be used once per round.", check.Error);
    }

    [Fact]
    public void AbilityWithoutCooldown_CanRepeat()
    {
        var check = Check(Profile(5, Jab), null, PlannedAction.Ability("jab", "B"), PlannedAction.Ability("jab", "B"));
        Assert.True(check.IsValid, check.Error);
    }

    [Fact]
    public void SingleTargetAbility_NeedsATarget()
    {
        var check = Check(Profile(5, PowerStrike), null, PlannedAction.Ability("power_strike"));
        Assert.Equal("Power Strike needs one target.", check.Error);
    }

    [Fact]
    public void UnknownAbility_CostsNothing_SoTheResolverJustWastesIt()
    {
        Assert.Equal(0, RulesConfig.Default.ApCost(PlannedAction.Ability("nope", "B"), Profile(5)));
        Assert.Equal(1, PlanValidator.AffordableCount([PlannedAction.Ability("nope", "B")], Profile(1)));
    }

    // ----- Relations -----

    private static CombatantState C(string id, string team) =>
        new CombatantState(id, id, 20, CombatProfile.Default, new GridPosition(0, 0)) { Team = team };

    [Theory]
    [InlineData("party", "party", Relation.Ally)]
    [InlineData("party", "enemy", Relation.Enemy)]
    [InlineData("", "", Relation.Enemy)]       // no team: everyone's enemy
    [InlineData("party", "", Relation.Enemy)]
    public void Relation_ComesFromTeams(string a, string b, Relation expected) =>
        Assert.Equal(expected, Relations.Between(C("A", a), C("B", b)));

    [Fact]
    public void Relation_WithYourself_IsSelf() =>
        Assert.Equal(Relation.Self, Relations.Between(C("A", "party"), C("A", "party")));

    [Theory]
    [InlineData(TargetFilter.Enemy, false, "B", false)]
    [InlineData(TargetFilter.Enemy, false, "E", true)]
    [InlineData(TargetFilter.Ally, false, "B", true)]
    [InlineData(TargetFilter.Ally, false, "A", false)]
    [InlineData(TargetFilter.Ally, true, "A", true)]
    [InlineData(TargetFilter.Any, false, "E", true)]
    [InlineData(TargetFilter.Any, false, "A", false)]
    public void Filter_MatchesByRelation(TargetFilter filter, bool includeSelf, string targetId, bool expected)
    {
        var byId = new Dictionary<string, CombatantState>
        {
            ["A"] = C("A", "party"), ["B"] = C("B", "party"), ["E"] = C("E", "enemy"),
        };
        var targeting = new Targeting(TargetKind.Combatant, filter, 1, IncludeSelf: includeSelf);
        Assert.Equal(expected, Relations.Matches(byId["A"], byId[targetId], targeting));
    }

    // ----- Damage spec -----

    private static AbilityDefinition Strike(DamageSpec damage, AttackKind roll = AttackKind.Melee, int accuracyBonus = 0) => new(
        "strike", "Strike", ApCost: 1, Cooldown: 0, Tags: [],
        new Targeting(TargetKind.Combatant, TargetFilter.Enemy, Range: 1),
        [new AttackEffect(roll, damage, accuracyBonus)]);

    private static AttackOutcome StrikeOnce(AbilityDefinition ability, params int[] dice)
    {
        var attacker = new CombatantState("A", "A", 20, Profile(5, ability) with { BaseDamage = DamageDie.D4 }, new GridPosition(0, 0)) { Team = "party" };
        var target = new CombatantState("B", "B", 20, CombatProfile.Default, new GridPosition(1, 0)) { Team = "enemy" };
        var plans = new Dictionary<string, IReadOnlyList<PlannedAction>> { ["A"] = [PlannedAction.Ability("strike", "B")] };
        var outcome = RoundResolver.Resolve(new EncounterState(20, 20, [attacker, target]), plans, new ScriptedDice(dice));
        return Assert.Single(outcome.Ticks[0].Attacks);
    }

    [Fact]
    public void WeaponDie_UsesTheCastersDie_PlusBonus_TimesMultiplier()
    {
        var hit = StrikeOnce(Strike(new DamageSpec(Bonus: 1, Multiplier: 2)), 15, 3); // d4 = 3 → (3 + 1) × 2
        Assert.Equal(8, hit.Damage);
        Assert.Equal("strike", hit.AbilityId);
        Assert.Equal(ActionType.MeleeAttack, hit.AttackType);
    }

    [Fact]
    public void FixedDie_IsRolledInsteadOfTheWeapon()
    {
        var hit = StrikeOnce(Strike(new DamageSpec(new DamageDie(10))), 15, 9); // a d4 can't roll 9
        Assert.Equal(9, hit.Damage);
    }

    [Fact]
    public void AccuracyBonus_IsAddedToTheRoll()
    {
        var hit = StrikeOnce(Strike(DamageSpec.Weapon, accuracyBonus: 3), 7, 2); // 7 + 3 = 10 ≥ dodge 10
        Assert.True(hit.Hit);
        Assert.Equal(3, hit.Roll!.Accuracy);
    }

    [Fact]
    public void NoRollAttack_AlwaysHits_WithoutAD20()
    {
        var hit = StrikeOnce(Strike(DamageSpec.Weapon, AttackKind.None), 2); // only the damage die
        Assert.True(hit.Hit);
        Assert.Null(hit.Roll);
        Assert.Equal(ActionType.Ability, hit.AttackType);
    }

    // ----- Multi-target & healing (PR 2) -----

    private static RoundOutcome Round(IReadOnlyList<CombatantState> combatants, string casterId, PlannedAction action, params int[] dice) =>
        RoundResolver.Resolve(new EncounterState(20, 20, combatants),
            new Dictionary<string, IReadOnlyList<PlannedAction>> { [casterId] = [action] }, new ScriptedDice(dice));

    private static CombatantState Unit(string id, string team, int x, int y, int hp = 20, CombatantStatus status = CombatantStatus.Standing,
        params AbilityDefinition[] abilities) =>
        new(id, id, hp, Profile(5, abilities) with { CanBeDowned = true }, new GridPosition(x, y), status,
            status == CombatantStatus.Downed ? 2 : 0) { Team = team };

    [Fact]
    public void AutoTargets_AreCappedAtCount_NearestFirst()
    {
        var sweep = new AbilityDefinition("sweep", "Sweep", 1, 0, [],
            new Targeting(TargetKind.Auto, TargetFilter.Enemy, Range: 3, Count: 2),
            [new AttackEffect(AttackKind.None, new DamageSpec(Bonus: 1))]);
        var outcome = Round(
            [Unit("A", "party", 5, 5, abilities: sweep), Unit("Far", "enemy", 8, 5), Unit("Near", "enemy", 6, 5), Unit("Mid", "enemy", 5, 7)],
            "A", PlannedAction.Ability("sweep"), 1, 1); // two damage dice, no d20 (auto-hit)
        Assert.Equal(["Near", "Mid"], outcome.Ticks[0].AbilityUses.Single().TargetIds);
        Assert.Equal(["Near", "Mid"], outcome.Ticks[0].Attacks.Select(a => a.TargetId));
    }

    private static AbilityDefinition Heal(bool canRevive = true, TargetFilter filter = TargetFilter.Ally) => new(
        "heal", "Heal", 1, 0, [], new Targeting(TargetKind.Combatant, filter, Range: 2, IncludeSelf: true),
        [new HealEffect(null, Bonus: 5, canRevive)]);

    [Fact]
    public void Heal_OnAStableAlly_RevivesThem()
    {
        var outcome = Round([Unit("H", "party", 0, 0, abilities: Heal()), Unit("B", "party", 1, 0, 0, CombatantStatus.Stable)],
            "H", PlannedAction.Ability("heal", "B")); // no die: a flat 5
        var heal = Assert.Single(outcome.Ticks[0].Heals);
        Assert.True(heal.Revived);
        Assert.Equal((0, 5), (heal.TargetHpBefore, heal.TargetHpAfter));
        Assert.Equal(CombatantStatus.Standing, outcome.Next.Combatants.Single(c => c.Id == "B").Status);
        Assert.Equal(["B"], outcome.RevivedThisRound);
    }

    [Fact]
    public void Heal_ThatCantRevive_IsWastedOnADownedAlly()
    {
        var outcome = Round([Unit("H", "party", 0, 0, abilities: Heal(canRevive: false)), Unit("B", "party", 1, 0, 0, CombatantStatus.Downed)],
            "H", PlannedAction.Ability("heal", "B"));
        Assert.Equal(WastedReason.TargetDefeated, Assert.Single(outcome.Ticks[0].Heals).WastedReason);
        Assert.Equal(0, outcome.Next.Combatants.Single(c => c.Id == "B").Hp);
    }

    [Fact]
    public void Heal_OnTheDead_IsWasted()
    {
        var outcome = Round([Unit("H", "party", 0, 0, abilities: Heal()), Unit("B", "party", 1, 0, 0, CombatantStatus.Dead)],
            "H", PlannedAction.Ability("heal", "B"));
        Assert.Equal(WastedReason.TargetDefeated, Assert.Single(outcome.Ticks[0].Heals).WastedReason);
        Assert.Empty(outcome.RevivedThisRound);
    }

    [Fact]
    public void Heal_OnYourself_NeedsIncludeSelf_AndIgnoresRange()
    {
        var outcome = Round([Unit("H", "party", 0, 0, hp: 10, abilities: Heal())], "H", PlannedAction.Ability("heal", "H"));
        Assert.Equal(15, outcome.Next.Combatants.Single().Hp);
    }

    [Fact]
    public void Heal_OnAnEnemy_IsTheWrongSide()
    {
        var outcome = Round([Unit("H", "party", 0, 0, abilities: Heal()), Unit("E", "enemy", 1, 0, hp: 10)],
            "H", PlannedAction.Ability("heal", "E"));
        Assert.Equal(WastedReason.InvalidTargetType, Assert.Single(outcome.Ticks[0].Heals).WastedReason);
        Assert.Equal(10, outcome.Next.Combatants.Single(c => c.Id == "E").Hp);
    }

    [Fact]
    public void DownedAllyHeal_OnAStandingAlly_IsWasted()
    {
        var outcome = Round([Unit("H", "party", 0, 0, abilities: Heal(filter: TargetFilter.DownedAlly)), Unit("B", "party", 1, 0, hp: 10)],
            "H", PlannedAction.Ability("heal", "B"));
        Assert.Equal(WastedReason.TargetNotDowned, Assert.Single(outcome.Ticks[0].Heals).WastedReason);
    }

    [Theory]
    [InlineData(TargetKind.Combatants, 2, new[] { "B", "C" }, null)]
    [InlineData(TargetKind.Combatants, 2, new[] { "B", "C", "D" }, "Volley can target at most 2.")]
    [InlineData(TargetKind.Combatants, 2, new[] { "B", "B" }, "Volley can't target the same combatant twice.")]
    [InlineData(TargetKind.Auto, null, new string[0], null)]
    [InlineData(TargetKind.Auto, null, new[] { "B" }, "Volley doesn't take a target.")]
    public void MultiTargetShapes_AreChecked(TargetKind kind, int? count, string[] targets, string? error)
    {
        var volley = new AbilityDefinition("volley", "Volley", 2, 0, [],
            new Targeting(kind, TargetFilter.Enemy, Range: 5, Count: count), [new AttackEffect(AttackKind.Ranged, DamageSpec.Weapon)]);
        var action = new PlannedAction(ActionType.Ability, AbilityId: "volley", TargetCombatantIds: targets.Length == 0 ? null : targets);
        Assert.Equal(error, Check(Profile(5, volley), null, action).Error);
    }
}
