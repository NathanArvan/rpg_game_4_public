using RpgGame.Api.Contracts.Hub;
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
        "stabilize" => ActionType.Stabilize,
        "ability" => ActionType.Ability,
        "disengage" => ActionType.Disengage,
        _ => null,
    };

    /// <summary>A wire queue entry as a rules action, or null for an unknown type.</summary>
    public static PlannedAction? ToPlannedAction(PlannedActionEntry entry) =>
        ParseActionType(entry.Type) is { } type
            ? new PlannedAction(
                type,
                entry.TargetCell is { } cell ? new GridPosition(cell.X, cell.Y) : null,
                entry.TargetCombatantId,
                type == ActionType.Ability ? entry.AbilityId : null,
                type == ActionType.Ability && entry.TargetCombatantIds is { Count: > 0 } ids ? ids : null)
            : null;

    /// <summary>A rules action as a wire queue entry (the reverse of <see cref="ToPlannedAction"/>).</summary>
    public static PlannedActionEntry ToEntry(PlannedAction action) => new(
        ToWire(action.Type),
        action.TargetCell is { } cell ? new PositionDto(cell.X, cell.Y) : null,
        action.TargetCombatantId,
        action.AbilityId,
        action.TargetCombatantIds);

    public static string ToWire(ActionType type) => type switch
    {
        ActionType.Move => "move",
        ActionType.MeleeAttack => "melee_attack",
        ActionType.RangedAttack => "ranged_attack",
        ActionType.Defend => "defend",
        ActionType.Stabilize => "stabilize",
        ActionType.Ability => "ability",
        ActionType.Disengage => "disengage",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    /// <summary>Statuses on the wire and in Redis: "standing", "downed", "stable", "dead" (3B.5).</summary>
    public static string ToWire(CombatantStatus status) => status switch
    {
        CombatantStatus.Standing => "standing",
        CombatantStatus.Downed => "downed",
        CombatantStatus.Stable => "stable",
        CombatantStatus.Dead => "dead",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static CombatantStatus? ParseStatus(string? wire) => wire switch
    {
        "standing" => CombatantStatus.Standing,
        "downed" => CombatantStatus.Downed,
        "stable" => CombatantStatus.Stable,
        "dead" => CombatantStatus.Dead,
        _ => null,
    };

    /// <summary>
    /// AP cost of every basic action type, keyed by its wire string, for the planning payload.
    /// Abilities carry their own cost (3B.3), so "ability" isn't listed.
    /// </summary>
    public static Dictionary<string, int> ApCosts(RulesConfig? config = null)
    {
        config ??= RulesConfig.Default;
        return RulesConfig.BasicActionTypes.ToDictionary(ToWire, config.ApCost);
    }

    /// <summary>Why an attack was wasted, as a wire string; null when it wasn't.</summary>
    public static string? ToWire(WastedReason reason) => reason switch
    {
        WastedReason.None => null,
        WastedReason.TargetDefeated => "target_defeated",
        WastedReason.OutOfMeleeReach => "out_of_melee_reach",
        WastedReason.InvalidTarget => "invalid_target",
        WastedReason.TargetNotDowned => "target_not_downed",
        WastedReason.OutOfRange => "out_of_range",
        WastedReason.OnCooldown => "on_cooldown",
        WastedReason.NotInLoadout => "not_in_loadout",
        WastedReason.InvalidTargetType => "invalid_target_type",
        WastedReason.NoTargets => "no_targets",
        WastedReason.Missed => "missed",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    /// <summary>"applied", "refreshed", "resisted" or "wasted" (3B.4).</summary>
    public static string ToWire(ConditionResultKind result) => result switch
    {
        ConditionResultKind.Applied => "applied",
        ConditionResultKind.Refreshed => "refreshed",
        ConditionResultKind.Resisted => "resisted",
        ConditionResultKind.Wasted => "wasted",
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
    };

    /// <summary>"opportunity" or "prepared" (3B.6).</summary>
    public static string ToWire(ReactionKind kind) => kind switch
    {
        ReactionKind.Opportunity => "opportunity",
        ReactionKind.Prepared => "prepared",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>"none", "blocked" or "fell" (3B.6, SPRINT_3B6 X14).</summary>
    public static string ToWire(StopReason reason) => reason switch
    {
        StopReason.None => "none",
        StopReason.Blocked => "blocked",
        StopReason.Fell => "fell",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    public static string ToWire(EffectType type) => type switch
    {
        EffectType.Defend => "defend",
        EffectType.Stabilize => "stabilize",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
