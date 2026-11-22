// AUTO-GENERATED — do not edit by hand.
// Source: C# records in server/src/RpgGame.Api/Contracts/Hub/
// Regenerate: npm run contracts:hub
export interface AbilityDto {
  id: string;
  name: string;
  description: string;
  apCost: number;
  cooldown: number;
  tags: string[];
  targeting: AbilityTargetingDto;
}

export interface AbilityTargetingDto {
  kind: string;
  filter: string;
  range: number;
  count: number | null;
  includeSelf: boolean;
}

export interface AbilityUseResult {
  combatantId: string;
  combatantName: string;
  abilityId: string;
  abilityName: string;
  targetIds: string[];
  wastedReason: string | null;
  feedText: string;
}

export interface ActionRejectedPayload {
  reason: string;
}

export interface CombatantDefeatedPayload {
  combatantId: string;
}

export interface CombatantDto {
  id: string;
  userId: string;
  characterId: string;
  characterName: string;
  currentHp: number;
  maxHp: number;
  combatantType: string;
  isAiControlled: boolean;
  profileId: string;
  profileName: string;
  movementRange: number;
  maxAp: number;
  abilities: AbilityDto[] | null;
}

export interface EncounterEndedPayload {
  encounterId: string;
  winningSide: string;
  finalRoundNumber: number;
}

export interface EncounterStartedPayload {
  encounterId: string;
  combatants: CombatantDto[];
  map: MapDto;
}

export interface PositionDto {
  x: number;
  y: number;
}

export interface MapDto {
  gridWidth: number;
  gridHeight: number;
  positions: { [key: string]: PositionDto };
  fogOfWar: string[];
}

export interface MonsterAddedPayload {
  monster: PlanningCombatantDto;
  spawnPosition: PositionDto;
}

export interface PlannedActionEntry {
  type: string;
  targetCell: PositionDto | null;
  targetCombatantId: string | null;
  abilityId: string | null;
  targetCombatantIds: string[] | null;
}

export interface PlannedActionArray {
  actions: PlannedActionEntry[];
}

export interface PlanningPhaseStartedPayload {
  roundNumber: number;
  planningTimerDurationSeconds: number;
  combatants: PlanningCombatantDto[];
  apCosts: { [key: string]: number };
}

export interface PlanningCombatantDto {
  id: string;
  userId: string;
  characterName: string;
  currentHp: number;
  maxHp: number;
  isDefeated: boolean;
  combatantType: string;
  isAiControlled: boolean;
  profileId: string;
  profileName: string;
  movementRange: number;
  maxAp: number;
  status: string;
  bleedoutRoundsLeft: number;
  abilities: AbilityDto[] | null;
  cooldowns: { [key: string]: number } | null;
}

export interface PlayerLockedInPayload {
  characterName: string;
}

export interface AllLockedInPayload {
}

export interface PlayerPresenceChangedPayload {
  userId: string;
  displayName: string;
  characterId: string | null;
  isOnline: boolean;
}

export interface QuickBattlePresetDto {
  id: string;
  name: string;
  description: string;
}

export interface QuickBattleStartedResult {
  joinCode: string;
  campaignId: string;
  gameSessionId: string;
  presetId: string;
}

export interface ResolutionPayload {
  ticks: TickResult[];
  newRoundNumber: number;
  defeatedCombatantIds: string[];
  updatedCombatants: PlanningCombatantDto[];
  queues: CombatantQueueDto[];
  endOfRound: BleedoutResultDto[];
}

export interface BleedoutResultDto {
  combatantId: string;
  combatantName: string;
  roundsLeft: number;
  died: boolean;
  feedText: string;
}

export interface CombatantQueueDto {
  combatantId: string;
  combatantName: string;
  maxAp: number;
  actions: QueuedActionDto[];
}

export interface QueuedActionDto {
  type: string;
  apCost: number;
  targetCombatantId: string | null;
  targetCell: PositionDto | null;
  abilityId: string | null;
}

export interface TickResult {
  tickIndex: number;
  movements: MovementResult[];
  attacks: AttackResult[];
  effects: EffectResult[];
  abilityUses: AbilityUseResult[] | null;
}

export interface MovementResult {
  combatantId: string;
  combatantName: string;
  path: PositionDto[];
  finalPosition: PositionDto;
  wasBlocked: boolean;
  blockedByCombatantId: string | null;
}

export interface AttackResult {
  attackerId: string;
  attackerName: string;
  targetId: string;
  targetName: string;
  attackType: string;
  hit: boolean;
  damage: number;
  targetNewHp: number;
  targetDefeated: boolean;
  wasted: boolean;
  feedText: string;
  roll: AttackRollDto | null;
  wastedReason: string | null;
  targetStatus: string;
  finishingBlow: boolean;
  abilityId: string | null;
}

export interface AttackRollDto {
  d20: number;
  accuracy: number;
  targetDodge: number;
}

export interface EffectResult {
  combatantId: string;
  effectType: string;
  description: string;
  targetCombatantId: string | null;
  wastedReason: string | null;
}

export const HubEvents = {
  Events: {
    PlayerPresenceChanged: 'PlayerPresenceChanged',
    EncounterStarted: 'EncounterStarted',
    PlanningPhaseStarted: 'PlanningPhaseStarted',
    PlayerLockedIn: 'PlayerLockedIn',
    AllLockedIn: 'AllLockedIn',
    ActionRejected: 'ActionRejected',
    CombatantDefeated: 'CombatantDefeated',
    EncounterEnded: 'EncounterEnded',
    ResolutionCompleted: 'ResolutionCompleted',
    MonsterAdded: 'MonsterAdded',
  },
  Methods: {
    JoinSession: 'JoinSession',
    StartEncounter: 'StartEncounter',
    PlanAction: 'PlanAction',
    LockIn: 'LockIn',
    ForceResolve: 'ForceResolve',
    AddMonster: 'AddMonster',
    PlanEnemyAction: 'PlanEnemyAction',
    LockInEnemies: 'LockInEnemies',
    GetQuickBattlePresets: 'GetQuickBattlePresets',
    StartQuickBattle: 'StartQuickBattle',
  },
} as const;
