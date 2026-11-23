/**
 * Test data builders for hub contracts and client state (TESTING_ROADMAP item 4).
 *
 * Each builder returns a complete, valid object with neutral defaults; pass only the fields a
 * test cares about. When a contract gains a required field, add its default here and nowhere
 * else: specs (and the /preview sandbox) never spell out a whole contract object themselves.
 *
 * Names default to ids (`attackerName` = `attackerId`, `characterName` = `id`, …), since most
 * specs use the same string for both.
 */
import {
  AbilityConditionDto,
  AbilityDto,
  ActiveConditionDto,
  AbilityTargetingDto,
  AbilityUseResult,
  AttackResult,
  AttackRollDto,
  BleedoutResultDto,
  CombatantDto,
  CombatantQueueDto,
  ConditionDto,
  ConditionModifiersDto,
  ConditionResult,
  EffectResult,
  ExpiredConditionDto,
  HealResult,
  MovementResult,
  PlanningCombatantDto,
  PositionDto,
  QueuedActionDto,
  ResolutionPayload,
  TickResult,
} from '../contracts/hub-contracts.generated';
import { Combatant } from '../state/encounter/encounter.model';

// --- Abilities ---

/** Targeting defaults: one enemy in melee reach. */
export function makeTargeting(overrides: Partial<AbilityTargetingDto> = {}): AbilityTargetingDto {
  return { kind: 'combatant', filter: 'enemy', range: 1, count: null, includeSelf: false, ...overrides };
}

/**
 * An ability; defaults to a Power Strike-like melee attack (3 AP, cooldown 2). `targeting` may be
 * partial: it's merged over {@link makeTargeting}'s defaults.
 */
export function makeAbility(
  overrides: Partial<Omit<AbilityDto, 'targeting'>> & { targeting?: Partial<AbilityTargetingDto> } = {},
): AbilityDto {
  const { targeting, ...rest } = overrides;
  const id = rest.id ?? 'power_strike';
  return {
    id, name: id, description: '', apCost: 3, cooldown: 2, tags: [], move: null, appliesConditions: [],
    ...rest,
    targeting: makeTargeting(targeting),
  };
}

// --- Conditions (3B.4) ---

/** Modifiers that change nothing; override the ones a test needs. */
export function makeModifiers(overrides: Partial<ConditionModifiersDto> = {}): ConditionModifiersDto {
  return {
    accuracy: 0, dodge: 0, attackKind: 'any', damageDealt: 0, damageTaken: null, movement: 0, ap: 0, periodic: null, blocks: [],
    ...overrides,
  };
}

/** A condition definition; defaults to Marked (harmful, +2 damage taken). `modifiers` may be partial. */
export function makeCondition(
  overrides: Partial<Omit<ConditionDto, 'modifiers'>> & { modifiers?: Partial<ConditionModifiersDto> } = {},
): ConditionDto {
  const { modifiers, ...rest } = overrides;
  const id = rest.id ?? 'marked';
  return {
    id, name: id, description: '', harmful: true, tags: [],
    ...rest,
    modifiers: makeModifiers(modifiers ?? (rest.id ? {} : { damageTaken: { percent: 0, flat: 2, tags: [] } })),
  };
}

/** A condition on a combatant; defaults to Marked until the end of the round. */
export function makeActiveCondition(
  overrides: Partial<Omit<ActiveConditionDto, 'condition'>> & { condition?: Parameters<typeof makeCondition>[0] } = {},
): ActiveConditionDto {
  const { condition, ...rest } = overrides;
  return { roundsLeft: 0, sourceId: null, ...rest, condition: makeCondition(condition) };
}

/** A condition an ability applies; defaults to Marked for 1 round, no hit needed. */
export function makeAbilityCondition(
  overrides: Partial<Omit<AbilityConditionDto, 'condition'>> & { condition?: Parameters<typeof makeCondition>[0] } = {},
): AbilityConditionDto {
  const { condition, ...rest } = overrides;
  return { duration: 1, requiresHit: false, ...rest, condition: makeCondition(condition) };
}

/** One condition result in a tick; defaults to Marked applied by 'a' to 'b' with Mark, until the end of next round. */
export function makeConditionResult(overrides: Partial<ConditionResult> = {}): ConditionResult {
  const sourceId = overrides.sourceId ?? 'a';
  const targetId = overrides.targetId ?? 'b';
  const conditionId = overrides.conditionId ?? 'marked';
  return {
    sourceId, sourceName: sourceId, targetId, targetName: targetId, conditionId, conditionName: conditionId,
    harmful: true, abilityId: 'mark', result: 'applied', roundsLeft: 1, wastedReason: null, feedText: '',
    ...overrides,
  };
}

/** A condition that ran out at the end of the round; defaults to Marked on c1. */
export function makeExpiredCondition(overrides: Partial<ExpiredConditionDto> = {}): ExpiredConditionDto {
  const combatantId = overrides.combatantId ?? 'c1';
  const conditionId = overrides.conditionId ?? 'marked';
  return { combatantId, combatantName: combatantId, conditionId, conditionName: conditionId, feedText: '', ...overrides };
}

// --- Combatants ---

/** A combatant row as the server sends it in PlanningPhaseStarted / ResolutionCompleted. Standing, 10/10 HP, 4 AP. */
export function makePlanningCombatant(overrides: Partial<PlanningCombatantDto> = {}): PlanningCombatantDto {
  const id = overrides.id ?? 'c1';
  return {
    id, userId: 'me', characterName: id, currentHp: 10, maxHp: 10, isDefeated: false,
    combatantType: 'player', isAiControlled: false, profileId: 'test', profileName: 'Test',
    movementRange: 6, maxAp: 4, status: 'standing', bleedoutRoundsLeft: 0, abilities: [], cooldowns: {}, conditions: [],
    ...overrides,
  };
}

/** A combatant as the server sends it in EncounterStarted. */
export function makeCombatantDto(overrides: Partial<CombatantDto> = {}): CombatantDto {
  const id = overrides.id ?? 'c1';
  return {
    id, userId: 'me', characterId: '', characterName: id, currentHp: 10, maxHp: 10,
    combatantType: 'player', isAiControlled: false, profileId: 'test', profileName: 'Test',
    movementRange: 6, maxAp: 4, abilities: [], conditions: [],
    ...overrides,
  };
}

/** A combatant in EncounterState (the client model). */
export function makeCombatant(overrides: Partial<Combatant> = {}): Combatant {
  const id = overrides.id ?? 'c1';
  return {
    id, userId: 'me', characterId: '', characterName: id, currentHp: 10, maxHp: 10, isDefeated: false,
    status: 'standing', bleedoutRoundsLeft: 0, combatantType: 'player', isAiControlled: false,
    profileName: 'Test', movementRange: 6, maxAp: 4, abilities: [], cooldowns: {}, conditions: [],
    ...overrides,
  };
}

// --- Resolution: queues ---

/** One resolved queue entry; defaults to a 1-AP defend. */
export function makeQueuedAction(overrides: Partial<QueuedActionDto> = {}): QueuedActionDto {
  return { type: 'defend', apCost: 1, targetCombatantId: null, targetCell: null, abilityId: null, targetCombatantIds: null, ...overrides };
}

/** A combatant's resolved queue; `actions` defaults to none. */
export function makeQueue(overrides: Partial<CombatantQueueDto> = {}): CombatantQueueDto {
  const combatantId = overrides.combatantId ?? 'c1';
  return { combatantId, combatantName: combatantId, maxAp: 4, actions: [], ...overrides };
}

// --- Resolution: tick results ---

/** A move. `finalPosition` defaults to the last cell of `path`; `path` defaults to one step from (0,0) to (1,0). */
export function makeMove(overrides: Partial<MovementResult> = {}): MovementResult {
  const combatantId = overrides.combatantId ?? 'c1';
  const path: PositionDto[] = overrides.path ?? [{ x: 0, y: 0 }, { x: 1, y: 0 }];
  return {
    combatantId, combatantName: combatantId, path, finalPosition: path[path.length - 1],
    wasBlocked: false, blockedByCombatantId: null, abilityId: null,
    ...overrides,
  };
}

/** An attack; defaults to a basic melee miss with no roll shown, the target still standing at 10 HP. */
export function makeAttack(overrides: Partial<AttackResult> = {}): AttackResult {
  const attackerId = overrides.attackerId ?? 'a';
  const targetId = overrides.targetId ?? 'b';
  return {
    attackerId, attackerName: attackerId, targetId, targetName: targetId, attackType: 'melee_attack',
    hit: false, damage: 0, targetNewHp: 10, targetDefeated: false, wasted: false, feedText: '',
    roll: null, wastedReason: null, targetStatus: 'standing', finishingBlow: false, abilityId: null, moveBonus: 0,
    conditionDamage: 0,
    ...overrides,
  };
}

/** A hit check; conditions changed nothing unless overridden (3B.4). */
export function makeRoll(overrides: Partial<AttackRollDto> = {}): AttackRollDto {
  return { d20: 10, accuracy: 0, targetDodge: 10, conditionAccuracy: 0, conditionDodge: 0, ...overrides };
}

/** A heal; defaults to a Mend for 0 on a standing target at 10 HP. */
export function makeHeal(overrides: Partial<HealResult> = {}): HealResult {
  const healerId = overrides.healerId ?? 'a';
  const targetId = overrides.targetId ?? 'b';
  return {
    healerId, healerName: healerId, targetId, targetName: targetId, abilityId: 'mend',
    amount: 0, targetNewHp: 10, revived: false, targetStatus: 'standing', wastedReason: null, feedText: '',
    ...overrides,
  };
}

/** An effect; defaults to a defend. */
export function makeEffect(overrides: Partial<EffectResult> = {}): EffectResult {
  return { combatantId: 'c1', effectType: 'defend', description: '', targetCombatantId: null, wastedReason: null, ...overrides };
}

/** An ability activation; defaults to Power Strike with no targets. */
export function makeAbilityUse(overrides: Partial<AbilityUseResult> = {}): AbilityUseResult {
  const combatantId = overrides.combatantId ?? 'c1';
  const abilityId = overrides.abilityId ?? 'power_strike';
  return {
    combatantId, combatantName: combatantId, abilityId, abilityName: abilityId, targetIds: [], wastedReason: null, feedText: '',
    ...overrides,
  };
}

/** A tick with nothing in it; `tickIndex` defaults to 0. */
export function makeTick(overrides: Partial<TickResult> = {}): TickResult {
  return { tickIndex: 0, movements: [], attacks: [], effects: [], abilityUses: [], heals: [], conditions: [], ...overrides };
}

/** End-of-round bleedout for a downed combatant; defaults to one round fewer, 1 left. */
export function makeBleedout(overrides: Partial<BleedoutResultDto> = {}): BleedoutResultDto {
  const combatantId = overrides.combatantId ?? 'c1';
  return { combatantId, combatantName: combatantId, roundsLeft: 1, died: false, feedText: '', ...overrides };
}

/** A ResolutionCompleted payload: no ticks, round 2 next, nobody defeated or revived. */
export function makePayload(overrides: Partial<ResolutionPayload> = {}): ResolutionPayload {
  return {
    ticks: [], newRoundNumber: 2, defeatedCombatantIds: [], updatedCombatants: [], queues: [], endOfRound: [], revivedCombatantIds: [],
    expiredConditions: [],
    ...overrides,
  };
}
