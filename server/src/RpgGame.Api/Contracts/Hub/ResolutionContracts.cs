namespace RpgGame.Api.Contracts.Hub;

/// <param name="RevivedCombatantIds">
/// Combatants who were down when the round started and are standing again, healed back up (3B.3).
/// They plan and lock in from the next round.
/// </param>
public record ResolutionPayload(
    IReadOnlyList<TickResult> Ticks,
    int NewRoundNumber,
    IReadOnlyList<string> DefeatedCombatantIds,
    IReadOnlyList<PlanningCombatantDto> UpdatedCombatants,
    IReadOnlyList<CombatantQueueDto> Queues,
    IReadOnlyList<BleedoutResultDto> EndOfRound,
    IReadOnlyList<string>? RevivedCombatantIds = null);

/// <summary>
/// End of round for a combatant who was already downed when it started (3B.5): one bleedout
/// round fewer, or dead if none are left.
/// </summary>
public record BleedoutResultDto(
    string CombatantId,
    string CombatantName,
    int RoundsLeft,
    bool Died,
    string FeedText);

/// <summary>
/// The action queue a combatant resolved this round (cut to what its AP covers). Action i
/// resolved in tick i, unless the combatant was defeated before then.
/// </summary>
public record CombatantQueueDto(
    string CombatantId,
    string CombatantName,
    int MaxAp,
    IReadOnlyList<QueuedActionDto> Actions);

/// <param name="AbilityId">For "ability": which ability (3B.3).</param>
/// <param name="TargetCombatantIds">For an ability with several chosen targets.</param>
public record QueuedActionDto(
    string Type,
    int ApCost,
    string? TargetCombatantId,
    PositionDto? TargetCell,
    string? AbilityId = null,
    IReadOnlyList<string>? TargetCombatantIds = null);

/// <param name="AbilityUses">Abilities used this tick, one per activation, in plan order (3B.3).</param>
/// <param name="Heals">Heals this tick, resolved with the attacks and netted with their damage (3B.3).</param>
public record TickResult(
    int TickIndex,
    IReadOnlyList<MovementResult> Movements,
    IReadOnlyList<AttackResult> Attacks,
    IReadOnlyList<EffectResult> Effects,
    IReadOnlyList<AbilityUseResult>? AbilityUses = null,
    IReadOnlyList<HealResult>? Heals = null);

/// <summary>One heal on one target (3B.3).</summary>
/// <param name="Amount">HP rolled, before netting and the max-HP cap; 0 when wasted.</param>
/// <param name="TargetNewHp">Target HP after the tick's damage and healing.</param>
/// <param name="Revived">The target was down and is standing again.</param>
/// <param name="TargetStatus">"standing", "downed", "stable" or "dead" after the tick.</param>
/// <param name="WastedReason">Why it did nothing ("out_of_range", "target_defeated", …), or null.</param>
public record HealResult(
    string HealerId,
    string HealerName,
    string TargetId,
    string TargetName,
    string AbilityId,
    int Amount,
    int TargetNewHp,
    bool Revived,
    string TargetStatus,
    string? WastedReason,
    string FeedText);

/// <param name="AbilityId">The ability that made this move (Dash, Charge), or null for a plain move (3B.3).</param>
public record MovementResult(
    string CombatantId,
    string CombatantName,
    IReadOnlyList<PositionDto> Path,
    PositionDto FinalPosition,
    bool WasBlocked,
    string? BlockedByCombatantId,
    string? AbilityId = null);

/// <param name="AbilityId">The ability that made this attack, or null for a basic attack (3B.3).</param>
/// <param name="MoveBonus">Damage included in <paramref name="Damage"/> because the ability moved its caster far enough (Charge).</param>
public record AttackResult(
    string AttackerId,
    string AttackerName,
    string TargetId,
    string TargetName,
    string AttackType,
    bool Hit,
    int Damage,
    int TargetNewHp,
    bool TargetDefeated,
    bool Wasted,
    string FeedText,
    AttackRollDto? Roll,
    string? WastedReason,
    string TargetStatus,
    bool FinishingBlow,
    string? AbilityId = null,
    int MoveBonus = 0);

/// <summary>The hit check: hit when D20 + Accuracy ≥ TargetDodge. Null on a wasted attack.</summary>
public record AttackRollDto(
    int D20,
    int Accuracy,
    int TargetDodge);

/// <param name="TargetCombatantId">Stabilize: who was being stabilized.</param>
/// <param name="WastedReason">Stabilize: why it did nothing ("target_not_downed", "out_of_melee_reach", …), or null.</param>
public record EffectResult(
    string CombatantId,
    string EffectType,
    string Description,
    string? TargetCombatantId,
    string? WastedReason);
