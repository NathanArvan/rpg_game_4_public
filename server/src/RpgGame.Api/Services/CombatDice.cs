namespace RpgGame.Api.Services;

internal static class CombatDice
{
    public static int DexModifier(int dex) => (int)Math.Floor((dex - 10) / 2.0);

    public static int RollD20() => Random.Shared.Next(1, 21);

    public static int RollDamage(string characterClass) => characterClass.ToLowerInvariant() switch
    {
        "fighter" or "paladin" or "ranger" => Random.Shared.Next(1, 9), // d8
        "rogue" or "bard" => Random.Shared.Next(1, 7),                  // d6
        _ => Random.Shared.Next(1, 5),                                   // d4
    };

    public static string DamageDiceString(string characterClass) => characterClass.ToLowerInvariant() switch
    {
        "fighter" or "paladin" or "ranger" => "1d8",
        "rogue" or "bard" => "1d6",
        _ => "1d4",
    };
}
