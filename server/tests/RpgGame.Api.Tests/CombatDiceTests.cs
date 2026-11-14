using RpgGame.Api.Services;
using RpgGame.Rules;

namespace RpgGame.Api.Tests;

public class CombatDiceTests
{
    private static readonly IDiceRoller Dice = new RandomDice();

    [Fact]
    public void RollDamage_Fighter_ReturnsOneThroughEight()
    {
        for (int i = 0; i < 10; i++)
            Assert.InRange(CombatDice.RollDamage(Dice, "Fighter"), 1, 8);
    }

    [Fact]
    public void RollDamage_Rogue_ReturnsOneThroughSix()
    {
        for (int i = 0; i < 10; i++)
            Assert.InRange(CombatDice.RollDamage(Dice, "Rogue"), 1, 6);
    }

    [Fact]
    public void RollDamage_Wizard_ReturnsOneThroughFour()
    {
        for (int i = 0; i < 10; i++)
            Assert.InRange(CombatDice.RollDamage(Dice, "Wizard"), 1, 4);
    }

    [Fact]
    public void RollD20_ReturnsOneToTwenty()
    {
        for (int i = 0; i < 10; i++)
            Assert.InRange(CombatDice.RollD20(Dice), 1, 20);
    }

    [Fact]
    public void DexModifier_CalculatesCorrectly()
    {
        Assert.Equal(0, CombatDice.DexModifier(10));
        Assert.Equal(2, CombatDice.DexModifier(14));
        Assert.Equal(-1, CombatDice.DexModifier(8));
    }
}
