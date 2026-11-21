namespace RpgGame.Rules;

/// <summary>
/// Tunable rule constants shared by every combatant. Per-combatant values (accuracy, dodge,
/// movement range, AP budget, …) live on <see cref="CombatProfile"/>.
/// </summary>
public sealed record RulesConfig
{
    public static RulesConfig Default { get; } = new();

    /// <summary>Max Chebyshev distance for a melee attack, checked after movement.</summary>
    public int MeleeReach { get; init; } = 1;

    /// <summary>AP cost of one Move (roadmap 3B.2). All of a round's moves share the mover's MovementRange cells.</summary>
    public int MoveApCost { get; init; } = 1;

    /// <summary>AP cost of one melee attack.</summary>
    public int MeleeApCost { get; init; } = 2;

    /// <summary>AP cost of one ranged attack.</summary>
    public int RangedApCost { get; init; } = 2;

    /// <summary>AP cost of one Defend.</summary>
    public int DefendApCost { get; init; } = 1;

    /// <summary>AP cost of one Stabilize (3B.5): the same as an attack, so rescuing competes with attacking.</summary>
    public int StabilizeApCost { get; init; } = 2;

    /// <summary>
    /// Rounds a Downed combatant has before dying: allies get this many planning phases to stabilize
    /// them (the round they fell in doesn't count). Flat for now; to come from a stat such as
    /// Hardiness once character stats exist (DEFEAT_AND_INCAPACITATION.md).
    /// </summary>
    public int BleedoutRounds { get; init; } = 2;

    public int ApCost(ActionType type) => type switch
    {
        ActionType.Move => MoveApCost,
        ActionType.MeleeAttack => MeleeApCost,
        ActionType.RangedAttack => RangedApCost,
        ActionType.Defend => DefendApCost,
        ActionType.Stabilize => StabilizeApCost,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown action type."),
    };
}
