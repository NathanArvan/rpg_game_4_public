namespace RpgGame.Api.Contracts.CombatProfiles;

/// <summary>A combat profile preset (3B.1), for pick lists (characters, GM-added monsters). Served by GET /api/combat-profiles.</summary>
public record CombatProfileDto(
    string Id,
    string Name,
    string Description,
    string Side,
    int MaxHp,
    int MovementRange,
    int Speed,
    int MeleeAccuracy,
    int RangedAccuracy,
    int Dodge,
    string BaseDamage);
