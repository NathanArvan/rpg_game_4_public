namespace RpgGame.Rules;

/// <param name="Next">Encounter state after the round: final positions, HP and defeated flags.</param>
/// <param name="DefeatedThisRound">Combatants defeated during this round, in the order they were detected.</param>
public sealed record RoundOutcome(
    EncounterState Next,
    IReadOnlyList<TickOutcome> Ticks,
    IReadOnlyList<string> DefeatedThisRound);

public sealed record TickOutcome(
    int Index,
    IReadOnlyList<MovementOutcome> Movements,
    IReadOnlyList<AttackOutcome> Attacks,
    IReadOnlyList<EffectOutcome> Effects);

/// <param name="Path">Cells actually traversed, starting with the origin cell.</param>
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
    /// <summary>The target was defeated in an earlier round or tick, or does not exist.</summary>
    TargetDefeated,
    /// <summary>Melee target was beyond reach after this tick's movement.</summary>
    OutOfMeleeReach,
}

/// <param name="TargetHpBefore">Target HP at the start of this tick's attack phase.</param>
/// <param name="TargetHpAfter">Target HP after all of this tick's damage was netted.</param>
/// <param name="TargetDefeated">Target was defeated by this tick's attack phase.</param>
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
    WastedReason WastedReason)
{
    public bool Wasted => WastedReason != WastedReason.None;
}

public enum EffectType
{
    Defend,
}

public sealed record EffectOutcome(string CombatantId, EffectType Type);
