using RpgGame.Rules;

namespace RpgGame.Api.Services;

internal static class CombatDice
{
    public static int DexModifier(int dex) => CombatMath.DexModifier(dex);

    public static int RollD20(IDiceRoller dice) => dice.RollD20();

    public static int RollDamage(IDiceRoller dice, string characterClass) =>
        dice.Roll(CombatMath.DamageDieForClass(characterClass).Sides);

    public static string DamageDiceString(string characterClass) =>
        CombatMath.DamageDieForClass(characterClass).ToString();
}
