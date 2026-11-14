using RpgGame.Rules;

namespace RpgGame.Rules.Tests;

public class CombatMathTests
{
    [Theory]
    [InlineData(10, 0)]
    [InlineData(11, 0)]
    [InlineData(14, 2)]
    [InlineData(8, -1)]
    [InlineData(9, -1)]
    [InlineData(1, -5)]
    public void DexModifier_FloorsHalfTheDifference(int dex, int expected) =>
        Assert.Equal(expected, CombatMath.DexModifier(dex));

    [Theory]
    [InlineData("Fighter", 8)]
    [InlineData("paladin", 8)]
    [InlineData("RANGER", 8)]
    [InlineData("Rogue", 6)]
    [InlineData("bard", 6)]
    [InlineData("Wizard", 4)]
    [InlineData("cleric", 4)]
    [InlineData("", 4)]
    public void DamageDieForClass_UsesClassTable(string cls, int sides) =>
        Assert.Equal(new DamageDie(sides), CombatMath.DamageDieForClass(cls));

    [Theory]
    [InlineData("d8", 8)]
    [InlineData("1d6", 6)]
    [InlineData(" D4 ", 4)]
    public void DamageDie_Parse(string text, int sides) => Assert.Equal(sides, DamageDie.Parse(text).Sides);

    [Theory]
    [InlineData("8")]
    [InlineData("2d6")]
    [InlineData("d0")]
    [InlineData("dx")]
    public void DamageDie_ParseRejectsInvalid(string text) => Assert.Throws<FormatException>(() => DamageDie.Parse(text));

    [Fact]
    public void DamageDie_ToString_IsOneDN() => Assert.Equal("1d8", DamageDie.D8.ToString());

    [Fact]
    public void GridPosition_ChebyshevDistance()
    {
        Assert.Equal(3, new GridPosition(0, 0).ChebyshevDistanceTo(new GridPosition(3, 2)));
        Assert.Equal(1, new GridPosition(5, 5).ChebyshevDistanceTo(new GridPosition(4, 6)));
        Assert.Equal(0, new GridPosition(2, 2).ChebyshevDistanceTo(new GridPosition(2, 2)));
    }
}
