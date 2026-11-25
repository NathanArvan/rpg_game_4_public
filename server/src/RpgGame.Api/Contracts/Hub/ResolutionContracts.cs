namespace RpgGame.Api.Contracts.Hub;

/// <param name="RevivedCombatantIds">
/// Combatants who were down when the round started and are standing again, healed back up (3B.3).
/// They plan and lock in from the next round.
/// </param>
/// <param name="ExpiredConditions">Conditions that ran out at the end of the round (3B.4).</param>
/// <param name="Periodic">Periodic damage and healing at the end of the round, before bleedout (3B.4 PR 2):
/// combatant order, then the order the conditions were applied.</param>
/// <param name="UnusedReactions">Prepared reactions still watching at the end of the round: they never fired (3B.6).</param>
public record ResolutionPayload(
    IReadOnlyList<TickResult> Ticks,
    int NewRoundNumber,
    IReadOnlyList<string> DefeatedCombatantIds,
    IReadOnlyList<PlanningCombatantDto> UpdatedCombatants,
    IReadOnlyList<CombatantQueueDto> Queues,
    IReadOnlyList<BleedoutResultDto> EndOfRound,
    IReadOnlyList<string>? RevivedCombatantIds = null,
    IReadOnlyList<ExpiredConditionDto>? ExpiredConditions = null,
    IReadOnlyList<PeriodicResultDto>? Periodic = null,
    IReadOnlyList<UnusedReactionDto>? UnusedReactions = null);

/// <summary>A prepared reaction that was watching at the end of the round and never fired (3B.6). No AP refund.</summary>
public record UnusedReactionDto(
    string CombatantId,
    string CombatantName,
    string AbilityId,
    string AbilityName,
    string FeedText);

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
/// <param name="Conditions">Conditions applied this tick (3B.4): Defend's Guarded at the start of the tick,
/// then abilities' conditions in the Effects phase.</param>
/// <param name="Reactions">Reactions this tick (3B.6), in resolution order: by movement step, then mover, then reactor.</param>
/// <param name="Watching">Combatants whose prepared reaction started watching at the start of this tick (3B.6).</param>
public record TickResult(
    int TickIndex,
    IReadOnlyList<MovementResult> Movements,
    IReadOnlyList<AttackResult> Attacks,
    IReadOnlyList<EffectResult> Effects,
    IReadOnlyList<AbilityUseResult>? AbilityUses = null,
    IReadOnlyList<HealResult>? Heals = null,
    IReadOnlyList<ConditionResult>? Conditions = null,
    IReadOnlyList<BlockedActionResult>? Blocked = null,
    IReadOnlyList<ReactionResult>? Reactions = null,
    IReadOnlyList<WatchingResult>? Watching = null);

/// <summary>
/// One reaction (3B.6): <see cref="ReactorId"/> reacted to <see cref="TriggerId"/>. An opportunity attack fires when the
/// trigger steps out of the reactor's reach; a prepared one when its trigger is met.
/// </summary>
/// <param name="Kind">"opportunity" or "prepared".</param>
/// <param name="AbilityId">The prepared ability that fired; null for an opportunity attack.</param>
/// <param name="Step">The movement step it fired on, from 1 (the trigger's first step is 1); 0 for one that fired outside movement.</param>
/// <param name="Attack">The attack it made, if any. Its target HP is the HP after that step's reactions.</param>
public record ReactionResult(
    string ReactorId,
    string ReactorName,
    string TriggerId,
    string TriggerName,
    string Kind,
    string? AbilityId,
    string? AbilityName,
    int Step,
    AttackResult? Attack,
    string FeedText);

/// <summary>
/// A combatant whose prepared reaction started watching at the start of this tick (3B.6): it watches until it fires, the
/// combatant falls or is Stunned, or the round ends. Its cooldown has started.
/// </summary>
public record WatchingResult(
    string CombatantId,
    string CombatantName,
    string AbilityId,
    string AbilityName,
    string FeedText);

/// <summary>
/// A queued action a condition stopped this tick (3B.4 PR 3): a Stunned combatant's action, or a Rooted
/// combatant's move. It didn't resolve (a blocked ability starts no cooldown).
/// </summary>
/// <param name="ActionType">The blocked action's wire type ("move", "melee_attack", "ability", …).</param>
/// <param name="AbilityId">For a blocked ability, which one.</param>
/// <param name="Block">"all" (can't act) or "move" (can't move).</param>
public record BlockedActionResult(
    string CombatantId,
    string CombatantName,
    string ActionType,
    string? AbilityId,
    string ConditionId,
    string ConditionName,
    string Block,
    string FeedText);

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
/// <param name="StopReason">"none", "blocked" (someone in the way) or "fell" (a reaction dropped it mid-path, 3B.6).</param>
/// <param name="Disengaging">A Disengage: it provoked no opportunity attacks (3B.6).</param>
public record MovementResult(
    string CombatantId,
    string CombatantName,
    IReadOnlyList<PositionDto> Path,
    PositionDto FinalPosition,
    bool WasBlocked,
    string? BlockedByCombatantId,
    string? AbilityId = null,
    string StopReason = "none",
    bool Disengaging = false);

/// <param name="AbilityId">The ability that made this attack, or null for a basic attack (3B.3).</param>
/// <param name="MoveBonus">Damage included in <paramref name="Damage"/> because the ability moved its caster far enough (Charge).</param>
/// <param name="ConditionDamage">How much conditions changed <paramref name="Damage"/>, signed: the attacker's damage dealt plus
/// the target's damage taken (3B.4).</param>
/// <param name="InterceptedFromId">The attack was meant for this combatant and an Intercept took it (3B.6 PR 3; null until then).</param>
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
    int MoveBonus = 0,
    int ConditionDamage = 0,
    string? InterceptedFromId = null);

/// <summary>The hit check: hit when D20 + Accuracy ≥ TargetDodge. Null on a wasted attack.</summary>
/// <param name="Accuracy">Everything added to the d20, conditions included.</param>
/// <param name="TargetDodge">The target's Dodge, conditions included.</param>
/// <param name="ConditionAccuracy">The part of <paramref name="Accuracy"/> from the attacker's conditions (3B.4).</param>
/// <param name="ConditionDodge">The part of <paramref name="TargetDodge"/> from the target's conditions (3B.4).</param>
/// <param name="ReactionDodge">The part of <paramref name="TargetDodge"/> from a prepared reaction such as Parry (3B.6 PR 3; 0 until then).</param>
public record AttackRollDto(
    int D20,
    int Accuracy,
    int TargetDodge,
    int ConditionAccuracy = 0,
    int ConditionDodge = 0,
    int ReactionDodge = 0);

/// <param name="TargetCombatantId">Stabilize: who was being stabilized.</param>
/// <param name="WastedReason">Stabilize: why it did nothing ("target_not_downed", "out_of_melee_reach", …), or null.</param>
public record EffectResult(
    string CombatantId,
    string EffectType,
    string Description,
    string? TargetCombatantId,
    string? WastedReason);
