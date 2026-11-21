namespace RpgGame.Rules;

/// <param name="Next">Encounter state after the round: final positions, HP and defeated flags.</param>
/// <param name="DefeatedThisRound">Combatants defeated during this round, in the order they were detected.</param>
/// <param name="ResolvedPlans">The queues the round actually resolved, in plan order: each one cut to what its
/// combatant could afford. Queue position i resolved in tick i (unless the combatant was defeated first).</param>
public sealed record RoundOutcome(
    EncounterState Next,
    IReadOnlyList<TickOutcome> Ticks,
    IReadOnlyList<string> DefeatedThisRound,
    IReadOnlyDictionary<string, IReadOnlyList<PlannedAction>> ResolvedPlans);

public sealed record TickOutcome(
    int Index,
    IReadOnlyList<MovementOutcome> Movements,
    IReadOnlyList<AttackOutcome> Attacks,
    IReadOnlyList<EffectOutcome> Effects);

/// <param name="Path">Cells actually traversed, starting with the origin cell. A single cell means the combatant stayed put.</param>
/// <param name="BlockedById">Who stopped this mover.</param>
public sealed record MovementOutcome(
    string CombatantId,
    IReadOnlyList<GridPosition> Path,
    GridPosition FinalPosition,
    bool WasBlocked,
    string? BlockedById);

public enum WastedReason
{
    None,
    /// <summary>The target was defeated in an earlier round or tick.</summary>
    TargetDefeated,
    /// <summary>Melee target was beyond reach after this tick's movement, or attacker or target has no map position.</summary>
    OutOfMeleeReach,
    /// <summary>The target id is not a combatant in this encounter.</summary>
    InvalidTarget,
}

/// <summary>The hit check: the attack hits when <c>D20 + Accuracy</c> is at least <c>TargetDodge</c>.</summary>
public sealed record AttackRoll(int D20, int Accuracy, int TargetDodge)
{
    public int Total => D20 + Accuracy;
}

/// <param name="TargetHpBefore">Target HP at the start of this tick's attack phase.</param>
/// <param name="TargetHpAfter">Target HP after all of this tick's damage was netted.</param>
/// <param name="TargetDefeated">Target was defeated by this tick's attack phase.</param>
/// <param name="Roll">The hit check, or null for a wasted attack (nothing was rolled).</param>
public sealed record AttackOutcome(
    string AttackerId,
    string TargetId,
    ActionType AttackType,
    bool Hit,
    int Damage,
    bool IsCrit,
    int TargetHpBefore,
    int TargetHpAfter,
    bool TargetDefeated,
    WastedReason WastedReason,
    AttackRoll? Roll = null)
{
    public bool Wasted => WastedReason != WastedReason.None;
}

public enum EffectType
{
    Defend,
}

public sealed record EffectOutcome(string CombatantId, EffectType Type);
