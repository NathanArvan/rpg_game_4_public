namespace RpgGame.Api.Services;

internal static class CombatDice
{
    public static int DexModifier(int dex) => (int)Math.Floor((dex - 10) / 2.0);

    public static int DamageSides(string characterClass) => characterClass.ToLowerInvariant() switch
    {
        "fighter" or "paladin" or "ranger" => 8,
        "rogue" or "bard" => 6,
        _ => 4,
    };

    public static int RollD20(IDiceRoller dice) => dice.RollD20();

    public static int RollDamage(IDiceRoller dice, string characterClass) => dice.Roll(DamageSides(characterClass));

    public static string DamageDiceString(string characterClass) => $"1d{DamageSides(characterClass)}";
}
