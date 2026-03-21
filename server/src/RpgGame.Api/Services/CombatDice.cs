namespace RpgGame.Api.Services;

internal static class CombatDice
{
    public static int RollD20() => Random.Shared.Next(1, 21);

    // damageBase 2 → 1d6, 3 → 1d8, 4 → 1d10
    public static int RollDamage(int damageBase) => Random.Shared.Next(1, damageBase * 2 + 3);

    public static string DamageDiceString(int damageBase) => $"1d{damageBase * 2 + 2}";
}
