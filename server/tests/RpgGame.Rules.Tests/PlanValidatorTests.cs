using RpgGame.Rules;

namespace RpgGame.Rules.Tests;

public partial class PlanValidatorTests
{
    private static CombatProfile Profile(int move = 6, int ap = 4) =>
        CombatProfile.Default with { MovementRange = move, MaxAp = ap };

    private static PlanCheck Check(CombatProfile profile, params PlannedAction[] queue) =>
        PlanValidator.Validate(new GridPosition(0, 0), profile, queue, 20, 20);

    [Fact]
    public void DefaultCosts_Move1_Attacks2_Defend1()
    {
        var c = RulesConfig.Default;
        Assert.Equal((1, 2, 2, 1),
            (c.ApCost(ActionType.Move), c.ApCost(ActionType.MeleeAttack), c.ApCost(ActionType.RangedAttack), c.ApCost(ActionType.Defend)));
    }

    [Fact]
    public void QueueWithinBudget_IsValid_AndReportsApSpent()
    {
        var check = Check(Profile(), PlannedAction.Move(2, 0), PlannedAction.Melee("B"), PlannedAction.Defend());
        Assert.True(check.IsValid);
        Assert.Equal(4, check.ApSpent);
        Assert.Equal(2, check.MoveCellsUsed);
    }

    [Fact]
    public void QueueOverBudget_IsRejected()
    {
        var check = Check(Profile(ap: 4), PlannedAction.Melee("B"), PlannedAction.Melee("B"), PlannedAction.Move(1, 0));
        Assert.False(check.IsValid);
        Assert.Contains("Not enough AP", check.Error);
        Assert.Contains("5 AP", check.Error);
    }

    [Fact]
    public void SecondMove_IsMeasuredFromWhereTheFirstEnds()
    {
        // (0,0) → (3,0) → (3,3): 3 + 3 = 6 cells, although (3,3) is only 3 from the start.
        Assert.True(Check(Profile(move: 6), PlannedAction.Move(3, 0), PlannedAction.Move(3, 3)).IsValid);
        var tooFar = Check(Profile(move: 5), PlannedAction.Move(3, 0), PlannedAction.Move(3, 3));
        Assert.False(tooFar.IsValid);
        Assert.Contains("maximum range (5 squares)", tooFar.Error);
    }

    [Fact]
    public void MoveBackToStart_StillCostsCells()
    {
        var check = Check(Profile(move: 3), PlannedAction.Move(2, 0), PlannedAction.Move(0, 0));
        Assert.False(check.IsValid);
    }

    [Fact]
    public void MoveOffTheGrid_IsRejected()
    {
        var check = Check(Profile(), PlannedAction.Move(-1, 0));
        Assert.Contains("out of bounds", check.Error);
    }

    [Fact]
    public void MoveWithNoPosition_IsRejected()
    {
        var check = PlanValidator.Validate(null, Profile(), [PlannedAction.Move(1, 0)], 20, 20);
        Assert.Equal("Current position not found.", check.Error);
    }

    [Theory]
    [InlineData(4, 2)]
    [InlineData(3, 1)]
    [InlineData(1, 0)]
    public void AffordableCount_StopsAtTheFirstActionThatDoesNotFit(int maxAp, int expected)
    {
        PlannedAction[] queue = [PlannedAction.Melee("B"), PlannedAction.Melee("B"), PlannedAction.Defend()];
        Assert.Equal(expected, PlanValidator.AffordableCount(queue, Profile() with { MaxAp = maxAp }));
    }
}
