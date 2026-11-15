namespace RpgGame.Rules;

/// <summary>
/// Tunable rule constants shared by every combatant. Per-combatant values (accuracy, dodge,
/// movement range, …) live on <see cref="CombatProfile"/>.
/// </summary>
public sealed record RulesConfig
{
    public static RulesConfig Default { get; } = new();

    /// <summary>Max Chebyshev distance for a melee attack, checked after movement.</summary>
    public int MeleeReach { get; init; } = 1;
}
