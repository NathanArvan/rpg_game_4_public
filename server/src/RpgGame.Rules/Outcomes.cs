namespace RpgGame.Rules;

/// <param name="Next">Encounter state after the round: final positions, HP and defeated flags.</param>
/// <param name="DefeatedThisRound">Combatants who stopped standing this round (went Downed or died), in the order detected.</param>
/// <param name="ResolvedPlans">The queues the round actually resolved, in plan order: each one cut to what its
/// combatant could afford. Queue position i resolved in tick i (unless the combatant was defeated first).</param>
public sealed record RoundOutcome(
    EncounterState Next,
    IReadOnlyList<TickOutcome> Ticks,
    IReadOnlyList<string> DefeatedThisRound,
    IReadOnlyDictionary<string, IReadOnlyList<PlannedAction>> ResolvedPlans,
    IReadOnlyList<BleedoutOutcome> EndOfRound);

/// <summary>
/// End of round for one combatant who was already Downed when the round started (3B.5): its
/// bleedout countdown went down by one, and it died if that reached 0.
/// </summary>
public sealed record BleedoutOutcome(string CombatantId, int RoundsLeft, bool Died);

public sealed record TickOutcome(
    int Index,
    IReadOnlyList<MovementOutcome> Movements,
    IReadOnlyList<AttackOutcome> Attacks,
    IReadOnlyList<EffectOutcome> Effects)
{
    /// <summary>Abilities used this tick (3B.3), one entry per activation, in plan order.</summary>
    public IReadOnlyList<AbilityUse> AbilityUses { get; init; } = [];
}

/// <summary>
/// One ability activation (3B.3): who used what, on whom. Its effects' results are in the tick's
/// other lists, tagged with the same ability id. <see cref="WastedReason"/> is set only when the
/// activation itself failed (not in the loadout, on cooldown); a target that turned out out of
/// range or already defeated is reported on that target's attack instead.
/// </summary>
/// <param name="StartedCooldown">The use put the ability on cooldown (every use that wasn't refused).</param>
public sealed record AbilityUse(
    string CombatantId,
    string AbilityId,
    IReadOnlyList<string> TargetIds,
    WastedReason WastedReason = WastedReason.None,
    bool StartedCooldown = false)
{
    public bool Wasted => WastedReason != WastedReason.None;
}

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
    /// <summary>The target is dead, or went down earlier this round (finishing blows must be planned, 3B.5).</summary>
    TargetDefeated,
    /// <summary>Melee target was beyond reach after this tick's movement, or attacker or target has no map position.</summary>
    OutOfMeleeReach,
    /// <summary>The target id is not a combatant in this encounter.</summary>
    InvalidTarget,
    /// <summary>Stabilize: the target wasn't Downed when it resolved (standing, already stable, or dead).</summary>
    TargetNotDowned,
    /// <summary>Ability: the target was beyond the ability's range after this tick's movement (3B.3).</summary>
    OutOfRange,
    /// <summary>Ability: still on cooldown (planning rejects this; the resolver only guards).</summary>
    OnCooldown,
    /// <summary>Ability: not in the combatant's loadout (planning rejects this; the resolver only guards).</summary>
    NotInLoadout,
    /// <summary>Ability: the target is on the wrong side for the ability's filter (e.g. an ally for an enemy-only attack).</summary>
    InvalidTargetType,
    /// <summary>Ability: nobody matched its targeting when it resolved (3B.3 PR 2).</summary>
    NoTargets,
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
/// <param name="TargetStatus">The target's status after this tick's attacks (3B.5): Downed or Dead if it fell.</param>
/// <param name="FinishingBlow">The target was already Downed or Stable: a hit kills it outright, with no damage roll.</param>
/// <param name="AbilityId">The ability that made this attack, or null for a basic melee / ranged attack (3B.3).</param>
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
    AttackRoll? Roll = null,
    CombatantStatus TargetStatus = CombatantStatus.Standing,
    bool FinishingBlow = false,
    string? AbilityId = null)
{
    public bool Wasted => WastedReason != WastedReason.None;
}

public enum EffectType
{
    Defend,
    Stabilize,
}

/// <param name="TargetId">Stabilize: who was being stabilized.</param>
/// <param name="WastedReason">Stabilize: why it did nothing (None when it worked).</param>
public sealed record EffectOutcome(
    string CombatantId,
    EffectType Type,
    string? TargetId = null,
    WastedReason WastedReason = WastedReason.None)
{
    public bool Wasted => WastedReason != WastedReason.None;
}
