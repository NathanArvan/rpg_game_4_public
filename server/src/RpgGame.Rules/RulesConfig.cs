namespace RpgGame.Rules;

/// <summary>Tunable rule constants. Defaults are the Phase 3A placeholder values (DESIGN_SPIKE Decision 4).</summary>
public sealed record RulesConfig
{
    public static RulesConfig Default { get; } = new();

    /// <summary>An attack hits when d20 + accuracy bonus is at least this ("targetDodge").</summary>
    public int HitThreshold { get; init; } = 10;

    /// <summary>Added to every hit roll.</summary>
    public int AccuracyBonus { get; init; } = 0;

    /// <summary>
    /// Max cells per move. Enforced at planning time by PlanningService, NOT re-checked by the
    /// resolver (quirk 3). Here so all rule constants live in one place.
    /// </summary>
    public int MovementRange { get; init; } = 6;

    /// <summary>Max Chebyshev distance for a melee attack, checked after movement.</summary>
    public int MeleeReach { get; init; } = 1;
}
