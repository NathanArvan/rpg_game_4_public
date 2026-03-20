using RpgGame.Api.Services;

namespace RpgGame.Api.Tests;

public class CombatDiceTests
{
    [Fact]
    public void RollDamage_Fighter_ReturnsOneThroughEight()
    {
        for (int i = 0; i < 10; i++)
            Assert.InRange(CombatDice.RollDamage("Fighter"), 1, 8);
    }

    [Fact]
    public void RollDamage_Rogue_ReturnsOneThroughSix()
    {
        for (int i = 0; i < 10; i++)
            Assert.InRange(CombatDice.RollDamage("Rogue"), 1, 6);
    }

    [Fact]
    public void RollDamage_Wizard_ReturnsOneThroughFour()
    {
        for (int i = 0; i < 10; i++)
            Assert.InRange(CombatDice.RollDamage("Wizard"), 1, 4);
    }

    [Fact]
    public void RollD20_ReturnsOneToTwenty()
    {
        for (int i = 0; i < 10; i++)
            Assert.InRange(CombatDice.RollD20(), 1, 20);
    }

    [Fact]
    public void DexModifier_CalculatesCorrectly()
    {
        Assert.Equal(0, CombatDice.DexModifier(10));
        Assert.Equal(2, CombatDice.DexModifier(14));
        Assert.Equal(-1, CombatDice.DexModifier(8));
    }
}
