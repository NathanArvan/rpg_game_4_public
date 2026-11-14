using RpgGame.Rules;

namespace RpgGame.Rules.Tests;

public class DiceTests
{
    [Fact]
    public void ScriptedDice_ReturnsRollsInOrder_AndTracksRemaining()
    {
        var dice = new ScriptedDice(3, 17, 6);
        Assert.Equal(3, dice.Roll(4));
        Assert.Equal(17, dice.RollD20());
        Assert.Equal(1, dice.Remaining);
        Assert.Equal(6, dice.Roll(6));
        Assert.Equal(0, dice.Remaining);
    }

    [Fact]
    public void ScriptedDice_ThrowsWhenExhausted()
    {
        var dice = new ScriptedDice(10);
        dice.RollD20();
        var ex = Assert.Throws<InvalidOperationException>(() => dice.RollD20());
        Assert.Contains("exhausted", ex.Message);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(21, 20)]
    [InlineData(5, 4)]
    public void ScriptedDice_ThrowsOnImpossibleRoll(int scripted, int sides)
    {
        var dice = new ScriptedDice(scripted);
        Assert.Throws<InvalidOperationException>(() => dice.Roll(sides));
    }

    [Fact]
    public void ScriptedDice_DoesNotAliasCallerArray()
    {
        var rolls = new[] { 4 };
        var dice = new ScriptedDice(rolls);
        rolls[0] = 99;
        Assert.Equal(4, dice.Roll(4));
    }

    [Fact]
    public void SeededDice_SameSeedSameRolls()
    {
        var a = new SeededDice(42);
        var b = new SeededDice(42);
        var rollsA = Enumerable.Range(0, 50).Select(i => i % 2 == 0 ? a.RollD20() : a.Roll(8)).ToList();
        var rollsB = Enumerable.Range(0, 50).Select(i => i % 2 == 0 ? b.RollD20() : b.Roll(8)).ToList();
        Assert.Equal(rollsA, rollsB);
    }

    [Fact]
    public void RandomAndSeededDice_StayInRange()
    {
        IDiceRoller[] rollers = [new RandomDice(), new SeededDice(7)];
        foreach (var dice in rollers)
            for (int i = 0; i < 200; i++)
            {
                Assert.InRange(dice.RollD20(), 1, 20);
                Assert.InRange(dice.Roll(4), 1, 4);
                Assert.InRange(dice.Roll(8), 1, 8);
            }
    }
}
