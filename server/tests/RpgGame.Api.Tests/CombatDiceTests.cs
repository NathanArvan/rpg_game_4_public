using RpgGame.Api.Services;

namespace RpgGame.Api.Tests;

public class CombatDiceTests
{
    [Fact]
    public void RollDamage_DamageBase2_ReturnsOneThroughSix()
    {
        for (int i = 0; i < 10; i++)
            Assert.InRange(CombatDice.RollDamage(2), 1, 6);
    }

    [Fact]
    public void RollDamage_DamageBase3_ReturnsOneThroughEight()
    {
        for (int i = 0; i < 10; i++)
            Assert.InRange(CombatDice.RollDamage(3), 1, 8);
    }

    [Fact]
    public void RollDamage_DamageBase4_ReturnsOneThroughTen()
    {
        for (int i = 0; i < 10; i++)
            Assert.InRange(CombatDice.RollDamage(4), 1, 10);
    }

    [Fact]
    public void DamageDiceString_ReturnsCorrectNotation()
    {
        Assert.Equal("1d6",  CombatDice.DamageDiceString(2));
        Assert.Equal("1d8",  CombatDice.DamageDiceString(3));
        Assert.Equal("1d10", CombatDice.DamageDiceString(4));
    }

    [Fact]
    public void RollD20_ReturnsOneToTwenty()
    {
        for (int i = 0; i < 10; i++)
            Assert.InRange(CombatDice.RollD20(), 1, 20);
    }
}
