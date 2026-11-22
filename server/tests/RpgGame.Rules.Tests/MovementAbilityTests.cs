using RpgGame.Rules;

namespace RpgGame.Rules.Tests;

/// <summary>Movement abilities (3B.3 PR 3): plan checks for Dash and Charge, and the charge destination.</summary>
public class MovementAbilityTests
{
    private static readonly AbilityDefinition Dash = new(
        "dash", "Dash", ApCost: 1, Cooldown: 2, Tags: ["movement"],
        new Targeting(TargetKind.Self, TargetFilter.Ally, Range: 0),
        [new MoveEffect(MoveTo.Cell, MaxCells: 3, UsesMovementBudget: false)]);

    private static readonly AbilityDefinition Charge = new(
        "charge", "Charge", ApCost: 3, Cooldown: 2, Tags: ["melee", "movement"],
        new Targeting(TargetKind.Combatant, TargetFilter.Enemy, Range: 6),
        [
            new MoveEffect(MoveTo.Target, MaxCells: 6),
            new AttackEffect(AttackKind.Melee, new DamageSpec(BonusIfMoved: new MovedBonus(2, 3))),
        ]);

    private static CombatProfile Profile(int ap = 5, int move = 6) =>
        (CombatProfile.Default with { MaxAp = ap, MovementRange = move }) with { Abilities = [Dash, Charge] };

    private static PlanCheck Check(CombatProfile profile, IReadOnlyDictionary<string, GridPosition>? positions, params PlannedAction[] queue) =>
        PlanValidator.Validate(new GridPosition(0, 0), profile, queue, 20, 20, positions: positions);

    [Fact]
    public void Dash_IsOffBudget()
    {
        var check = Check(Profile(), null, PlannedAction.Move(6, 0), PlannedAction.AbilityAt("dash", 9, 0));
        Assert.True(check.IsValid, check.Error);
        Assert.Equal(6, check.MoveCellsUsed);
    }

    [Fact]
    public void Dash_IsMeasuredFromWhereTheMovesEnd()
    {
        var check = Check(Profile(), null, PlannedAction.Move(6, 0), PlannedAction.AbilityAt("dash", 10, 0));
        Assert.Equal("Dash moves at most 3 squares.", check.Error);
    }

    [Fact]
    public void Dash_MovesWhereLaterMovesStart()
    {
        // Dash to (3,0), then a 6-cell walk from there reaches (9,0).
        var check = Check(Profile(), null, PlannedAction.AbilityAt("dash", 3, 0), PlannedAction.Move(9, 0));
        Assert.True(check.IsValid, check.Error);
    }

    [Fact]
    public void Dash_NeedsACell()
    {
        Assert.Equal("Dash needs a target cell.", Check(Profile(), null, PlannedAction.Ability("dash")).Error);
        Assert.Equal("Dash doesn't take a target.", Check(Profile(), null, new PlannedAction(ActionType.Ability,
            new GridPosition(1, 0), TargetCombatantId: "B", AbilityId: "dash")).Error);
    }

    [Fact]
    public void Dash_OffTheGrid_IsRejected()
    {
        Assert.Contains("out of bounds", Check(Profile(), null, PlannedAction.AbilityAt("dash", -1, 0)).Error);
    }

    [Fact]
    public void ACellOnAnAbilityThatDoesNotMove_IsRejected()
    {
        var check = Check(Profile(), null, new PlannedAction(ActionType.Ability, new GridPosition(1, 0), TargetCombatantId: "B", AbilityId: "charge"));
        Assert.Equal("Charge doesn't take a target cell.", check.Error);
    }

    [Fact]
    public void Charge_CountsItsEstimatedCellsTowardTheBudget()
    {
        // Target 5 cells away: the charge walks 4 to get next to it, leaving 2 of 6 for later moves.
        var positions = new Dictionary<string, GridPosition> { ["B"] = new(5, 0) };
        var check = Check(Profile(ap: 6), positions, PlannedAction.Ability("charge", "B"));
        Assert.True(check.IsValid, check.Error);
        Assert.Equal(4, check.MoveCellsUsed);

        var tooFar = Check(Profile(ap: 6), positions, PlannedAction.Ability("charge", "B"), PlannedAction.Move(4, 3));
        Assert.Contains("exceed maximum range", tooFar.Error);
    }

    [Fact]
    public void Charge_AfterAWalk_IsCappedNotRejected()
    {
        var positions = new Dictionary<string, GridPosition> { ["B"] = new(12, 0) };
        var check = Check(Profile(), positions, PlannedAction.Move(4, 0), PlannedAction.Ability("charge", "B"));
        Assert.True(check.IsValid, check.Error);
        Assert.Equal(6, check.MoveCellsUsed);
    }

    [Fact]
    public void DestinationNear_PicksTheClosestFreeCellInLineWithTheTarget()
    {
        var target = new GridPosition(5, 5);
        Assert.Equal(new GridPosition(5, 6), RoundResolver.DestinationNear(new(5, 9), target, 1, 20, 20, _ => true));
        // Taken: the next best, top-to-bottom then left-to-right among equals.
        Assert.Equal(new GridPosition(4, 6), RoundResolver.DestinationNear(new(5, 9), target, 1, 20, 20, c => c != new GridPosition(5, 6)));
        // Already in reach: stay.
        Assert.Equal(new GridPosition(6, 6), RoundResolver.DestinationNear(new(6, 6), target, 1, 20, 20, _ => true));
        // Surrounded.
        Assert.Null(RoundResolver.DestinationNear(new(5, 9), target, 1, 20, 20, _ => false));
    }
}
