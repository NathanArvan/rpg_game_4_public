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

    /// <summary>AP cost of every action type, keyed by its wire string, for the planning payload.</summary>
    public static Dictionary<string, int> ApCosts(RulesConfig? config = null)
    {
        config ??= RulesConfig.Default;
        return Enum.GetValues<ActionType>().ToDictionary(ToWire, config.ApCost);
    }

    /// <summary>Why an attack was wasted, as a wire string; null when it wasn't.</summary>
    public static string? ToWire(WastedReason reason) => reason switch
    {
        WastedReason.None => null,
        WastedReason.TargetDefeated => "target_defeated",
        WastedReason.OutOfMeleeReach => "out_of_melee_reach",
        WastedReason.InvalidTarget => "invalid_target",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    public static string ToWire(EffectType type) => type switch
    {
        EffectType.Defend => "defend",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
