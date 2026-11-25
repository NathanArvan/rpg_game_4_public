using RpgGame.Rules;

namespace RpgGame.Rules.Tests;

public class EnemyAiTests
{
    private static CombatantState C(string id, int x, int y, int hp = 20, bool defeated = false, int move = 6, int ap = CombatProfile.DefaultMaxAp) =>
        new(id, id, hp, CombatProfile.Default with { MovementRange = move, MaxAp = ap }, new GridPosition(x, y), defeated ? CombatantStatus.Dead : CombatantStatus.Standing);

    private static PlannedAction[] Q(params PlannedAction[] queue) => queue;

    /// <summary>Ids starting with "g" are goblins (enemy team, AI-controlled); everyone else is the party.</summary>
    private static Dictionary<string, IReadOnlyList<PlannedAction>> Plan(params CombatantState[] combatants)
    {
        var state = new EncounterState(20, 20, combatants);
        var teams = combatants.ToDictionary(c => c.Id, c => c.Id.StartsWith('g') ? "enemy" : "party");
        var ai = combatants.Where(c => c.Id.StartsWith('g')).Select(c => c.Id).ToList();
        return EnemyAi.PlanRound(state, teams, ai);
    }

    private static CombatantState OffMap(string id) => new(id, id, 20, CombatProfile.Default, null);

    [Fact]
    public void AiWithNoMapPosition_Defends()
    {
        var plans = Plan(C("hero", 0, 1), OffMap("g1"));
        Assert.Equal(Q(PlannedAction.Defend()), plans["g1"]);
    }

    [Fact]
    public void EnemyWithNoMapPosition_IsNotTargeted()
    {
        // The off-map hero would be "at (0,0)", adjacent, under the old rule; the AI goes for the real one.
        var plans = Plan(OffMap("ghost"), C("hero", 5, 1), C("g1", 1, 1));
        Assert.Equal(Q(PlannedAction.Move(4, 1), PlannedAction.Melee("hero")), plans["g1"]);
    }

    [Fact]
    public void AdjacentEnemy_IsAttackedInMelee_AsOftenAsApAllows()
    {
        // 4 AP, melee costs 2.
        var plans = Plan(C("hero", 5, 5), C("g1", 5, 6));
        Assert.Equal(Q(PlannedAction.Melee("hero"), PlannedAction.Melee("hero")), plans["g1"]);
    }

    [Fact]
    public void DiagonalNeighbour_CountsAsAdjacent()
    {
        var plans = Plan(C("hero", 5, 5), C("g1", 6, 6));
        Assert.Equal(ActionType.MeleeAttack, plans["g1"][0].Type);
    }

    [Fact]
    public void EnemyWithinMoveRange_MovesToClosestFreeCellNextToIt()
    {
        // Goblin at (5,10), hero at (5,5): closest neighbour of the hero is (5,6), 4 cells away.
        var plans = Plan(C("hero", 5, 5), C("g1", 5, 10));
        Assert.Equal(Q(PlannedAction.Move(5, 6), PlannedAction.Melee("hero")), plans["g1"]);
    }

    [Fact]
    public void EnemyBeyondMoveRange_MovesTheFullRangeAlongThePath()
    {
        // Players start on row 0, monsters on row 19: 19 rows apart, range 6.
        var plans = Plan(C("hero", 0, 0), C("g1", 0, 19));
        Assert.Equal(Q(PlannedAction.Move(0, 13)), plans["g1"]);
    }

    [Fact]
    public void TwoGoblins_DoNotPickTheSameCell()
    {
        var plans = Plan(C("hero", 5, 5), C("g1", 5, 9), C("g2", 6, 9));
        Assert.Equal(PlannedAction.Move(5, 6), plans["g1"][0]);
        var second = plans["g2"][0];
        Assert.Equal(ActionType.Move, second.Type);
        Assert.NotEqual(new GridPosition(5, 6), second.TargetCell);
        Assert.Equal(1, second.TargetCell!.Value.ChebyshevDistanceTo(new GridPosition(5, 5)));
    }

    [Fact]
    public void OccupiedCells_AreAvoided_IncludingDefeatedBodies()
    {
        // (5,6) holds a defeated ally; the goblin picks the next-closest free neighbour.
        var plans = Plan(C("hero", 5, 5), C("corpse", 5, 6, defeated: true), C("g1", 5, 9));
        var move = plans["g1"][0];
        Assert.Equal(ActionType.Move, move.Type);
        Assert.NotEqual(new GridPosition(5, 6), move.TargetCell);
        Assert.Equal(1, move.TargetCell!.Value.ChebyshevDistanceTo(new GridPosition(5, 5)));
    }

    [Fact]
    public void BlockedCellAtFullRange_StepsAroundIt()
    {
        // Far target; (0,13) holds a defeated body, so take the next cell just as close to the hero.
        var plans = Plan(C("hero", 0, 0), C("rock", 0, 13, defeated: true), C("g1", 0, 19));
        Assert.Equal(Q(PlannedAction.Move(1, 13)), plans["g1"]);
    }

    [Fact]
    public void PathThroughABody_IsAvoided()
    {
        // Straight up would walk through the body at (0,16); go one column over instead.
        var plans = Plan(C("hero", 0, 0), C("rock", 0, 16, defeated: true), C("g1", 0, 19));
        Assert.Equal(Q(PlannedAction.Move(1, 13)), plans["g1"]);
    }

    [Fact]
    public void TwoGoblinsSideBySide_DoNotCollideOnTheWay()
    {
        // The spawn layout: goblins on row 19 next to each other, hero far away on row 0. Walking
        // straight would have g2 step diagonally into g1's first cell, and equal Speed stops both.
        var plans = Plan(C("hero", 0, 0), C("g1", 0, 19), C("g2", 1, 19));
        Assert.Equal(Q(PlannedAction.Move(0, 13)), plans["g1"]);
        Assert.Equal(Q(PlannedAction.Move(1, 13)), plans["g2"]);

        var state = new EncounterState(20, 20, [C("hero", 0, 0), C("g1", 0, 19), C("g2", 1, 19)]);
        var outcome = RoundResolver.Resolve(state, plans, new ScriptedDice([]));
        Assert.All(outcome.Ticks[0].Movements, m => Assert.False(m.WasBlocked));
    }

    [Fact]
    public void NoCloserCell_DefendsInsteadOfWandering()
    {
        // Every cell next to the hero is taken by a body; the goblin is already 2 away, so no move
        // gets it closer. It holds its ground rather than walking off.
        var ring = new[] { (4, 4), (5, 4), (6, 4), (4, 5), (6, 5), (4, 6), (5, 6), (6, 6) }
            .Select((p, i) => C($"body{i}", p.Item1, p.Item2, defeated: true));
        var plans = Plan([C("hero", 5, 5), .. ring, C("g1", 5, 7)]);
        Assert.Equal(Q(PlannedAction.Defend()), plans["g1"]);
    }

    [Fact]
    public void TargetsNearestLivingEnemy_IgnoringDefeatedEnemiesAndAllies()
    {
        var plans = Plan(
            C("downed", 5, 6, defeated: true),
            C("far", 5, 0),
            C("g1", 5, 7),
            C("g2", 5, 8));
        // g1 ignores the defeated hero next to it and the goblin behind it, and heads for "far",
        // stepping around the body at (5,6) rather than walking straight through it.
        // (4,1) is next to "far", so the goblin strikes with the AP left after the walk.
        Assert.Equal(Q(PlannedAction.Move(4, 1), PlannedAction.Melee("far")), plans["g1"]);
    }

    [Fact]
    public void Tie_GoesToLowestHp()
    {
        var plans = Plan(C("healthy", 4, 5, hp: 20), C("wounded", 6, 5, hp: 3), C("g1", 5, 5));
        Assert.Equal(PlannedAction.Melee("wounded"), plans["g1"][0]);
    }

    [Fact]
    public void NoLivingEnemies_Defends()
    {
        var plans = Plan(C("hero", 5, 5, defeated: true), C("g1", 0, 0));
        Assert.Equal(Q(PlannedAction.Defend()), plans["g1"]);
    }

    [Fact]
    public void DefeatedAi_GetsNoPlan()
    {
        var plans = Plan(C("hero", 5, 5), C("g1", 5, 6, defeated: true));
        Assert.Empty(plans);
    }

    [Fact]
    public void SameInput_SamePlans()
    {
        CombatantState[] combatants = [C("a", 3, 2), C("b", 9, 1), C("g1", 4, 15), C("g2", 8, 17), C("g3", 12, 19)];
        var first = Plan(combatants);
        var second = Plan(combatants);
        Assert.Equal(first.Keys, second.Keys);
        foreach (var id in first.Keys) Assert.Equal(first[id], second[id]);
    }

    [Fact]
    public void DoesNotMutateInput()
    {
        CombatantState[] combatants = [C("hero", 0, 0), C("g1", 0, 19)];
        var before = combatants.ToList();
        Plan(combatants);
        Assert.Equal(before, combatants);
    }

    [Fact]
    public void Ai_WalksOnlyItsOwnMovementRange()
    {
        var slow = Plan(C("hero", 10, 0), C("g1", 10, 15, move: 2));
        Assert.Equal(Q(PlannedAction.Move(10, 13)), slow["g1"]);

        var fast = Plan(C("hero", 10, 0), C("g1", 10, 15, move: 9));
        Assert.Equal(Q(PlannedAction.Move(10, 6)), fast["g1"]);
    }

    // ----- Action points (3B.2) -----

    [Theory]
    [InlineData(1, 0)] // can't afford a melee: defends
    [InlineData(2, 1)]
    [InlineData(5, 2)]
    [InlineData(6, 3)]
    public void AdjacentEnemy_MeleeCountFollowsMaxAp(int maxAp, int melees)
    {
        var plans = Plan(C("hero", 5, 5), C("g1", 5, 6, ap: maxAp));
        if (melees == 0)
            Assert.Equal(Q(PlannedAction.Defend()), plans["g1"]);
        else
            Assert.Equal(Enumerable.Repeat(PlannedAction.Melee("hero"), melees), plans["g1"]);
    }

    [Fact]
    public void WalkEndingInReach_IsFollowedByMeleesWithTheApLeft()
    {
        // 5 AP: move (1) + melee (2) + melee (2).
        var plans = Plan(C("hero", 5, 5), C("g1", 5, 10, ap: 5));
        Assert.Equal(Q(PlannedAction.Move(5, 6), PlannedAction.Melee("hero"), PlannedAction.Melee("hero")), plans["g1"]);
    }

    [Fact]
    public void EveryAiQueue_FitsItsBudget()
    {
        CombatantState[] combatants = [C("a", 3, 2), C("b", 9, 1), C("g1", 4, 3, ap: 3), C("g2", 8, 6, ap: 5), C("g3", 12, 19)];
        var state = new EncounterState(20, 20, combatants);
        foreach (var (id, queue) in Plan(combatants))
        {
            var self = combatants.Single(c => c.Id == id);
            Assert.True(PlanValidator.Validate(self.Position, self.Profile, queue, 20, 20).IsValid, id);
        }
    }

    [Fact]
    public void IgnoresDownedHeroes_AndGoesForTheStandingOne()
    {
        // The downed hero is adjacent, the standing one further off: the AI doesn't finish anyone off (3B.5).
        var downed = C("downedHero", 5, 6, hp: 0) with { Status = CombatantStatus.Downed, BleedoutRoundsLeft = 2 };
        var plans = Plan(C("goblin", 5, 5), downed, C("hero", 5, 9));

        Assert.DoesNotContain(plans["goblin"], a => a.TargetCombatantId == "downedHero");
        Assert.Equal(ActionType.Move, plans["goblin"][0].Type);
    }

    [Fact]
    public void StunnedAi_IsNotPlanned_AndStillBlocksItsCell()
    {
        var stun = new ActiveCondition(new ConditionDefinition("stunned", "Stunned", true, [], new Modifiers(Blocks: [BlockKind.All])), 0);
        var stunned = C("g1", 1, 0) with { Conditions = [stun] };
        var plans = Plan(C("hero", 0, 0), stunned, C("g2", 5, 0));
        Assert.False(plans.ContainsKey("g1"));
        Assert.True(plans.ContainsKey("g2"));
    }

    [Fact]
    public void AiAtZeroEffectiveAp_IsNotPlanned()
    {
        var drained = new ActiveCondition(new ConditionDefinition("drained", "Drained", true, [], new Modifiers(Ap: -4)), 0);
        var plans = Plan(C("hero", 0, 0), C("g1", 1, 0) with { Conditions = [drained] });
        Assert.False(plans.ContainsKey("g1"));
    }

    [Fact]
    public void SlowedAi_WalksOnlyItsReducedBudget()
    {
        var slowed = new ActiveCondition(new ConditionDefinition("slowed", "Slowed", true, [], new Modifiers(Movement: -2)), 0);
        var plans = Plan(C("hero", 0, 0), C("g1", 10, 0) with { Conditions = [slowed] });
        Assert.Equal(Q(PlannedAction.Move(6, 0)), plans["g1"]);
    }
}
