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
}
