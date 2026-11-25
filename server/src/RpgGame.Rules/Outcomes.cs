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
    /// <summary>Conditions that ran out at the end of this round (3B.4), in combatant order.</summary>
    public IReadOnlyList<ExpiredCondition> ExpiredConditions { get; init; } = [];

    /// <summary>
    /// Periodic damage and healing at the end of this round (3B.4 PR 2, SPRINT_3B4 C11), before bleedout:
    /// combatant order, then the order the conditions were applied. Netted per combatant as one batch.
    /// </summary>
    public IReadOnlyList<PeriodicOutcome> PeriodicEffects { get; init; } = [];

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

    /// <summary>
    /// Conditions applied this tick (3B.4): Defend's Guarded at the start of the tick, then ability
    /// conditions in the Effects phase, in plan order then target order.
    /// </summary>
    public IReadOnlyList<ConditionOutcome> Conditions { get; init; } = [];

    /// <summary>
    /// Actions that didn't resolve this tick because a condition stopped them (3B.4 PR 3, SPRINT_3B4 C8): a
    /// Stunned combatant's queued action, or a Rooted combatant's plain move. In plan order.
    /// </summary>
    public IReadOnlyList<BlockedAction> Blocked { get; init; } = [];

    /// <summary>
    /// Reactions this tick (3B.6, SPRINT_3B6 X3), in resolution order: by movement step, then mover plan order,
    /// then reactor (combatant) order. Each resolved on the spot, at the step that provoked it.
    /// </summary>
    public IReadOnlyList<ReactionOutcome> Reactions { get; init; } = [];
}

/// <summary>What kind of reaction fired (3B.6).</summary>
public enum ReactionKind
{
    /// <summary>A passive opportunity attack: an enemy stepped out of the reactor's reach.</summary>
    Opportunity,
    /// <summary>A prepared reaction (an ability with <see cref="AbilityActivation.Prepared"/>, PR 2).</summary>
    Prepared,
}

/// <summary>
/// One reaction (3B.6, SPRINT_3B6 X1–X3): <paramref name="ReactorId"/> reacted to <paramref name="TriggerId"/>.
/// </summary>
/// <param name="AbilityId">The prepared ability that fired, or null for an opportunity attack.</param>
/// <param name="Step">The movement step it fired on, from 1 (the mover's step 1 is its first cell after the start).</param>
/// <param name="Attack">The attack it made. Its HP before / after are the target's HP around that step's batch.</param>
public sealed record ReactionOutcome(
    string ReactorId,
    string TriggerId,
    ReactionKind Kind,
    string? AbilityId,
    int Step,
    AttackOutcome? Attack);

/// <summary>Why a mover stopped short of the end of its path (3B.6, SPRINT_3B6 X14).</summary>
public enum StopReason
{
    /// <summary>It didn't stop short (or only ran out of budget or grid).</summary>
    None,
    /// <summary>Someone was in the way (<see cref="MovementOutcome.BlockedById"/>).</summary>
    Blocked,
    /// <summary>A reaction dropped it mid-path: it stays on the cell it fell on.</summary>
    Fell,
}

/// <summary>
/// A queued action a condition stopped (3B.4 PR 3): <paramref name="ConditionId"/> blocks every action
/// (<see cref="BlockKind.All"/>, Stunned) or moves (<see cref="BlockKind.Move"/>, Rooted). A blocked ability
/// isn't used, so its cooldown doesn't start.
/// </summary>
public sealed record BlockedAction(string CombatantId, PlannedAction Action, string ConditionId, BlockKind Block = BlockKind.All);

/// <summary>
/// One periodic effect at the end of a round (3B.4 PR 2): a condition's damage or healing on its bearer.
/// All of a combatant's periodic effects are netted together, so <see cref="HpBefore"/>, <see cref="HpAfter"/>
/// and <see cref="Status"/> are the same on each of that combatant's entries.
/// </summary>
/// <param name="Amount">HP rolled: damage after the bearer's damage-taken modifiers, or healing (≥ 0).</param>
/// <param name="ConditionDamage">How much the bearer's damage-taken modifiers changed the damage, signed (0 for heals).</param>
/// <param name="HpBefore">The bearer's HP before the end of round.</param>
/// <param name="HpAfter">The bearer's HP after all its periodic effects were netted (capped at MaxHp, never below 0).</param>
/// <param name="Status">The bearer's status afterwards: Downed or Dead if this made it fall.</param>
/// <param name="Fell">The bearer fell to these effects.</param>
public sealed record PeriodicOutcome(
    string CombatantId,
    string ConditionId,
    PeriodicKind Kind,
    int Amount,
    int HpBefore,
    int HpAfter,
    CombatantStatus Status,
    bool Fell = false,
    int ConditionDamage = 0);

/// <summary>A condition that ran out at the end of a round (3B.4).</summary>
public sealed record ExpiredCondition(string CombatantId, string ConditionId);

public enum ConditionResultKind
{
    /// <summary>The target didn't have it; now it does.</summary>
    Applied,
    /// <summary>The target already had it: it now lasts the longer of the two durations (SPRINT_3B4 C12).</summary>
    Refreshed,
    /// <summary>The target resisted it (ConditionResist, 3B.4 PR 2).</summary>
    Resisted,
    /// <summary>Nothing happened: see <see cref="ConditionOutcome.WastedReason"/>.</summary>
    Wasted,
}

/// <summary>One attempt to put a condition on one target (3B.4).</summary>
/// <param name="SourceId">Who applied it (the defender, for Guarded).</param>
/// <param name="AbilityId">The ability that applied it, or null for Defend's Guarded.</param>
/// <param name="RoundsLeft">Its rounds left afterwards (C5); 0 for a wasted or resisted one.</param>
/// <param name="WastedReason">Why nothing happened, for <see cref="ConditionResultKind.Wasted"/>:
/// out of range, wrong side, target down, or <see cref="WastedReason.Missed"/> (it needed a hit).</param>
public sealed record ConditionOutcome(
    string SourceId,
    string TargetId,
    string ConditionId,
    string? AbilityId,
    ConditionResultKind Result,
    int RoundsLeft,
    WastedReason WastedReason = WastedReason.None)
{
    public bool Wasted => Result == ConditionResultKind.Wasted;
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
/// <param name="AbilityId">The ability that made this move (Dash, Charge), or null for a plain move (3B.3).</param>
/// <param name="StopReason">Why it stopped short: blocked, or dropped by a reaction (3B.6).</param>
/// <param name="Disengaging">A Disengage (or an ability move that disengages): it provoked no opportunity attacks.</param>
public sealed record MovementOutcome(
    string CombatantId,
    IReadOnlyList<GridPosition> Path,
    GridPosition FinalPosition,
    bool WasBlocked,
    string? BlockedById,
    string? AbilityId = null,
    StopReason StopReason = StopReason.None,
    bool Disengaging = false);

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
    /// <summary>Condition: it needed the same ability's attack to hit this target, and it didn't (3B.4).</summary>
    Missed,
}

/// <summary>The hit check: the attack hits when <c>D20 + Accuracy</c> is at least <c>TargetDodge</c>.</summary>
/// <param name="Accuracy">Every bonus added to the d20, conditions included.</param>
/// <param name="TargetDodge">The target's Dodge, conditions included.</param>
public sealed record AttackRoll(int D20, int Accuracy, int TargetDodge)
{
    public int Total => D20 + Accuracy;

    /// <summary>The part of <see cref="Accuracy"/> that came from the attacker's conditions (3B.4).</summary>
    public int ConditionAccuracy { get; init; }

    /// <summary>The part of <see cref="TargetDodge"/> that came from the target's conditions (3B.4).</summary>
    public int ConditionDodge { get; init; }
}

/// <param name="TargetHpBefore">Target HP at the start of this tick's attack phase.</param>
/// <param name="TargetHpAfter">Target HP after all of this tick's damage was netted.</param>
/// <param name="TargetDefeated">Target was defeated by this tick's attack phase.</param>
/// <param name="Roll">The hit check, or null for a wasted attack (nothing was rolled).</param>
/// <param name="TargetStatus">The target's status after this tick's attacks (3B.5): Downed or Dead if it fell.</param>
/// <param name="FinishingBlow">The target was already Downed or Stable: a hit kills it outright, with no damage roll.</param>
/// <param name="AbilityId">The ability that made this attack, or null for a basic melee / ranged attack (3B.3).</param>
/// <param name="MoveBonus">Damage included in <paramref name="Damage"/> because the ability moved the caster far enough (Charge's <see cref="DamageSpec.BonusIfMoved"/>).</param>
/// <param name="ConditionDamage">How much conditions changed <paramref name="Damage"/> (3B.4): the attacker's damage
/// dealt plus the target's damage taken, signed (−6 resisted, +2 marked).</param>
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
    string? AbilityId = null,
    int MoveBonus = 0,
    int ConditionDamage = 0)
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
