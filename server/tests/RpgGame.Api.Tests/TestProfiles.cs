using RpgGame.Api.CombatProfiles;
using RpgGame.Domain.Entities;

namespace RpgGame.Api.Tests;

/// <summary>Combat profile helpers for tests.</summary>
internal static class TestProfiles
{
    /// <summary>The shipped profiles (CombatProfiles/profiles.json, copied to the test output).</summary>
    public static CombatProfileCatalog Catalog { get; } = CombatProfileCatalog.FromAppDirectory();

    /// <summary>Stored profile values; defaults are the 3A placeholders (CombatProfile.Default).</summary>
    public static CombatProfileValues Values(
        int maxHp = 20, int move = 6, int speed = 10, int melee = 0, int ranged = 0, int dodge = 10,
        string damage = "d8", string id = "test", string name = "Test", int? ap = null) => new()
    {
        ProfileId = id,
        ProfileName = name,
        MaxHp = maxHp,
        MovementRange = move,
        Speed = speed,
        MeleeAccuracy = melee,
        RangedAccuracy = ranged,
        Dodge = dodge,
        BaseDamage = damage,
        MaxAp = ap,
    };
}
