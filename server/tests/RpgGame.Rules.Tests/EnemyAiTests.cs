using RpgGame.Rules;

namespace RpgGame.Rules.Tests;

public class EnemyAiTests
{
    private static CombatantState C(string id, int x, int y, int hp = 20, bool defeated = false, int move = 6) =>
        new(id, id, hp, CombatProfile.Default with { MovementRange = move }, new GridPosition(x, y), defeated);

    /// <summary>Ids starting with "g" are goblins (enemy team, AI-controlled); everyone else is the party.</summary>
    private static Dictionary<string, PlannedAction> Plan(params CombatantState[] combatants)
    {
        var state = new EncounterState(20, 20, combatants);
        var teams = combatants.ToDictionary(c => c.Id, c => c.Id.StartsWith('g') ? "enemy" : "party");
        var ai = combatants.Where(c => c.Id.StartsWith('g')).Select(c => c.Id).ToList();
        return EnemyAi.PlanRound(state, teams, ai);
    }

    private static CombatantState OffMap(string id) => new(id, id, 20, CombatProfile.Default, null, false);

    [Fact]
    public void AiWithNoMapPosition_Defends()
    {
        var plans = Plan(C("hero", 0, 1), OffMap("g1"));
        Assert.Equal(PlannedAction.Defend(), plans["g1"]);
    }

    [Fact]
    public void EnemyWithNoMapPosition_IsNotTargeted()
    {
        // The off-map hero would be "at (0,0)", adjacent, under the old rule; the AI goes for the real one.
        var plans = Plan(OffMap("ghost"), C("hero", 5, 1), C("g1", 1, 1));
        Assert.Equal(PlannedAction.Move(4, 1), plans["g1"]);
    }

    [Fact]
    public void AdjacentEnemy_IsAttackedInMelee()
    {
        var plans = Plan(C("hero", 5, 5), C("g1", 5, 6));
        Assert.Equal(PlannedAction.Melee("hero"), plans["g1"]);
    }

    [Fact]
    public void DiagonalNeighbour_CountsAsAdjacent()
    {
        var plans = Plan(C("hero", 5, 5), C("g1", 6, 6));
        Assert.Equal(PlannedAction.Melee("hero"), plans["g1"]);
    }

    [Fact]
    public void EnemyWithinMoveRange_MovesToClosestFreeCellNextToIt()
    {
        // Goblin at (5,10), hero at (5,5): closest neighbour of the hero is (5,6), 4 cells away.
        var plans = Plan(C("hero", 5, 5), C("g1", 5, 10));
        Assert.Equal(PlannedAction.Move(5, 6), plans["g1"]);
    }

    [Fact]
    public void EnemyBeyondMoveRange_MovesTheFullRangeAlongThePath()
    {
        // Players start on row 0, monsters on row 19: 19 rows apart, range 6.
        var plans = Plan(C("hero", 0, 0), C("g1", 0, 19));
        Assert.Equal(PlannedAction.Move(0, 13), plans["g1"]);
    }

    [Fact]
    public void TwoGoblins_DoNotPickTheSameCell()
    {
        var plans = Plan(C("hero", 5, 5), C("g1", 5, 9), C("g2", 6, 9));
        Assert.Equal(PlannedAction.Move(5, 6), plans["g1"]);
        var second = plans["g2"];
        Assert.Equal(ActionType.Move, second.Type);
        Assert.NotEqual(new GridPosition(5, 6), second.TargetCell);
        Assert.Equal(1, second.TargetCell!.Value.ChebyshevDistanceTo(new GridPosition(5, 5)));
    }

    [Fact]
    public void OccupiedCells_AreAvoided_IncludingDefeatedBodies()
    {
        // (5,6) holds a defeated ally; the goblin picks the next-closest free neighbour.
        var plans = Plan(C("hero", 5, 5), C("corpse", 5, 6, defeated: true), C("g1", 5, 9));
        var move = plans["g1"];
        Assert.Equal(ActionType.Move, move.Type);
        Assert.NotEqual(new GridPosition(5, 6), move.TargetCell);
        Assert.Equal(1, move.TargetCell!.Value.ChebyshevDistanceTo(new GridPosition(5, 5)));
    }

    [Fact]
    public void BlockedCellAtFullRange_StepsAroundIt()
    {
        // Far target; (0,13) holds a defeated body, so take the next cell just as close to the hero.
        var plans = Plan(C("hero", 0, 0), C("rock", 0, 13, defeated: true), C("g1", 0, 19));
        Assert.Equal(PlannedAction.Move(1, 13), plans["g1"]);
    }

    [Fact]
    public void PathThroughABody_IsAvoided()
    {
        // Straight up would walk through the body at (0,16); go one column over instead.
        var plans = Plan(C("hero", 0, 0), C("rock", 0, 16, defeated: true), C("g1", 0, 19));
        Assert.Equal(PlannedAction.Move(1, 13), plans["g1"]);
    }

    [Fact]
    public void TwoGoblinsSideBySide_DoNotCollideOnTheWay()
    {
        // The spawn layout: goblins on row 19 next to each other, hero far away on row 0. Walking
        // straight would have g2 step diagonally into g1's first cell, and equal Speed stops both.
        var plans = Plan(C("hero", 0, 0), C("g1", 0, 19), C("g2", 1, 19));
        Assert.Equal(PlannedAction.Move(0, 13), plans["g1"]);
        Assert.Equal(PlannedAction.Move(1, 13), plans["g2"]);

        var state = new EncounterState(20, 20, [C("hero", 0, 0), C("g1", 0, 19), C("g2", 1, 19)]);
        var queues = plans.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<PlannedAction>)[kv.Value]);
        var outcome = RoundResolver.Resolve(state, queues, new ScriptedDice([]));
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
        Assert.Equal(PlannedAction.Defend(), plans["g1"]);
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
        Assert.Equal(PlannedAction.Move(4, 1), plans["g1"]);
    }

    [Fact]
    public void Tie_GoesToLowestHp()
    {
        var plans = Plan(C("healthy", 4, 5, hp: 20), C("wounded", 6, 5, hp: 3), C("g1", 5, 5));
        Assert.Equal(PlannedAction.Melee("wounded"), plans["g1"]);
    }

    [Fact]
    public void NoLivingEnemies_Defends()
    {
        var plans = Plan(C("hero", 5, 5, defeated: true), C("g1", 0, 0));
        Assert.Equal(PlannedAction.Defend(), plans["g1"]);
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
        Assert.Equal(first.ToList(), second.ToList());
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
        Assert.Equal(PlannedAction.Move(10, 13), slow["g1"]);

        var fast = Plan(C("hero", 10, 0), C("g1", 10, 15, move: 9));
        Assert.Equal(PlannedAction.Move(10, 6), fast["g1"]);
    }
}
