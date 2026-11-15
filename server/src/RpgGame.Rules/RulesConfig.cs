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

    public int ApCost(ActionType type) => type switch
    {
        ActionType.Move => MoveApCost,
        ActionType.MeleeAttack => MeleeApCost,
        ActionType.RangedAttack => RangedApCost,
        ActionType.Defend => DefendApCost,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown action type."),
    };
}
