using RpgGame.Rules;

namespace RpgGame.Api.Services;

/// <summary>Maps between the hub's wire strings and RpgGame.Rules enums.</summary>
internal static class RulesWireMapping
{
    public static ActionType? ParseActionType(string? wire) => wire switch
    {
        "move" => ActionType.Move,
        "melee_attack" => ActionType.MeleeAttack,
        "ranged_attack" => ActionType.RangedAttack,
        "defend" => ActionType.Defend,
        _ => null,
    };

    public static string ToWire(ActionType type) => type switch
    {
        ActionType.Move => "move",
        ActionType.MeleeAttack => "melee_attack",
        ActionType.RangedAttack => "ranged_attack",
        ActionType.Defend => "defend",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static string ToWire(EffectType type) => type switch
    {
        EffectType.Defend => "defend",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
