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
    IReadOnlyList<BleedoutOutcome> EndOfRound)
{
    /// <summary>
    /// Combatants who were out of the fight (Downed or Stable) when the round started and are
    /// standing again at its end, because a heal brought them back (3B.3, ABILITIES.md A8).
    /// Someone who went down and was healed back up within the round is in neither this list nor
    /// <see cref="DefeatedThisRound"/>.
    /// </summary>
    public IReadOnlyList<string> RevivedThisRound { get; init; } = [];
}

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

    /// <summary>Heals this tick (3B.3), resolved with the attacks, in plan order then target order.</summary>
    public IReadOnlyList<HealOutcome> Heals { get; init; } = [];
}

/// <summary>
/// One ability activation (3B.3): who used what, on whom. Its effects' results are in the tick's
/// other lists, tagged with the same ability id. <see cref="WastedReason"/> is set when the
/// activation itself failed: refused (not in the loadout, on cooldown) or, for an
/// <see cref="TargetKind.Auto"/> ability, nobody to hit (<see cref="WastedReason.NoTargets"/>, which
/// still starts the cooldown). A chosen target that turned out out of range or already defeated is
/// reported on that target's attack or heal instead.
/// </summary>
/// <param name="TargetIds">The chosen targets; for an <see cref="TargetKind.Auto"/> ability, the ones found when it resolved.</param>
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
    /// <summary>
    /// The target is dead, or went down earlier this round (finishing blows must be planned, 3B.5).
    /// Heals: the target is dead, or down and the heal can't revive.
    /// </summary>
    TargetDefeated,
    /// <summary>Melee target was beyond reach after this tick's movement, or attacker or target has no map position.</summary>
    OutOfMeleeReach,
    /// <summary>The target id is not a combatant in this encounter.</summary>
    InvalidTarget,
    /// <summary>Stabilize, or a heal that only targets downed allies: the target wasn't Downed when it resolved.</summary>
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

/// <summary>One heal on one target (3B.3), resolved in the Attacks phase with the tick's damage.</summary>
/// <param name="Amount">HP rolled (die + bonus), before netting and the MaxHp cap; 0 when wasted.</param>
/// <param name="TargetHpBefore">Target HP at the start of this tick's attack phase.</param>
/// <param name="TargetHpAfter">Target HP after all of this tick's damage and healing was netted (capped at MaxHp).</param>
/// <param name="Revived">The target was Downed or Stable and is standing again.</param>
/// <param name="TargetStatus">The target's status after this tick's attacks and heals.</param>
public sealed record HealOutcome(
    string HealerId,
    string TargetId,
    string AbilityId,
    int Amount,
    int TargetHpBefore,
    int TargetHpAfter,
    bool Revived,
    WastedReason WastedReason,
    CombatantStatus TargetStatus = CombatantStatus.Standing)
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
