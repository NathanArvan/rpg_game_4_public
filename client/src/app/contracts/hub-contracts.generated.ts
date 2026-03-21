// AUTO-GENERATED — do not edit by hand.
// Source: C# records in server/src/RpgGame.Api/Contracts/Hub/
// Regenerate: npm run contracts:hub
export interface ActionRejectedPayload {
  reason: string;
}

export interface AttackResolvedPayload {
  attackerId: string;
  attackerName: string;
  targetId: string;
  targetName: string;
  damage: number;
  targetNewHp: number;
  feedText: string;
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
}

export interface EncounterEndedPayload {
  encounterId: string;
}

export interface EncounterStartedPayload {
  encounterId: string;
  roundNumber: number;
  roundPhase: string;
  planningTimerExpiresAt: number;
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

export interface PlanningPhaseStartedPayload {
  encounterId: string;
  roundNumber: number;
  planningTimerExpiresAt: number;
}

export interface ActionPlannedPayload {
  combatantId: string;
  actionType: string;
}

export interface PlayerLockedInPayload {
  combatantId: string;
  characterName: string;
  isGmEnemies: boolean;
}

export interface AllLockedInPayload {
  encounterId: string;
}

export interface EnemyActionPlannedPayload {
  combatantId: string;
  actionType: string;
}

export interface PlayerPresenceChangedPayload {
  userId: string;
  displayName: string;
  characterId: string | null;
  isOnline: boolean;
}

export interface ResolutionStepDto {
  phase: string;
  actorId: string;
  actorName: string;
  actionType: string;
  targetId: string | null;
  targetName: string | null;
  damage: number | null;
  damageDice: string | null;
  newHp: number | null;
  newX: number | null;
  newY: number | null;
  defeated: boolean;
  feedText: string;
}

export interface ResolutionCompletedPayload {
  encounterId: string;
  roundNumber: number;
  steps: ResolutionStepDto[];
  updatedPositions: { [key: string]: PositionDto };
  encounterEnded: boolean;
}

export interface TokenMovedPayload {
  combatantId: string;
  x: number;
  y: number;
}

export const HubEvents = {
  Events: {
    PlayerPresenceChanged: 'PlayerPresenceChanged',
    EncounterStarted: 'EncounterStarted',
    TokenMoved: 'TokenMoved',
    ActionRejected: 'ActionRejected',
    AttackResolved: 'AttackResolved',
    CombatantDefeated: 'CombatantDefeated',
    EncounterEnded: 'EncounterEnded',
    PlanningPhaseStarted: 'PlanningPhaseStarted',
    ActionPlanned: 'ActionPlanned',
    PlayerLockedIn: 'PlayerLockedIn',
    AllLockedIn: 'AllLockedIn',
    ResolutionCompleted: 'ResolutionCompleted',
    EnemyActionPlanned: 'EnemyActionPlanned',
  },
  Methods: {
    JoinSession: 'JoinSession',
    StartEncounter: 'StartEncounter',
    PlanAction: 'PlanAction',
    LockIn: 'LockIn',
    ForceResolve: 'ForceResolve',
    PlanEnemyAction: 'PlanEnemyAction',
    LockInEnemies: 'LockInEnemies',
  },
} as const;
