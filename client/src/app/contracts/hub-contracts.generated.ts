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
}

export interface EncounterEndedPayload {
  encounterId: string;
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
  characterName: string;
  currentHp: number;
  maxHp: number;
  isDefeated: boolean;
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
  },
  Methods: {
    JoinSession: 'JoinSession',
    StartEncounter: 'StartEncounter',
    PlanAction: 'PlanAction',
    LockIn: 'LockIn',
    ForceResolve: 'ForceResolve',
  },
} as const;
