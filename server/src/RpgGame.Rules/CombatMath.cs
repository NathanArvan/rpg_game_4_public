namespace RpgGame.Rules;

/// <summary>Placeholder stat math for Phase 3A (D&D-style stats until 3B.1).</summary>
public static class CombatMath
{
    public static int DexModifier(int dex) => (int)Math.Floor((dex - 10) / 2.0);

    /// <summary>Class-based damage die: fighter/paladin/ranger d8, rogue/bard d6, everything else d4.</summary>
    public static DamageDie DamageDieForClass(string characterClass) => characterClass.ToLowerInvariant() switch
    {
        "fighter" or "paladin" or "ranger" => DamageDie.D8,
        "rogue" or "bard" => DamageDie.D6,
        _ => DamageDie.D4,
    };
}
