// AUTO-GENERATED — do not edit by hand.
// Source: C# records in server/src/RpgGame.Api/Contracts/Hub/
// Regenerate: npm run contracts:hub
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
}

export interface PlannedActionArray {
  actions: PlannedActionEntry[];
}

export interface PlanningPhaseStartedPayload {
  roundNumber: number;
  planningTimerDurationSeconds: number;
  combatants: PlanningCombatantDto[];
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
}

export interface TickResult {
  tickIndex: number;
  movements: MovementResult[];
  attacks: AttackResult[];
  effects: EffectResult[];
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
}

export interface EffectResult {
  combatantId: string;
  effectType: string;
  description: string;
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
