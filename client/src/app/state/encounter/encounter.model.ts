export interface Combatant {
  id: string;
  userId: string;
  characterId: string;
  characterName: string;
  currentHp: number;
  maxHp: number;
  isDefeated: boolean;
}

export interface PlannedAction {
  actionType: string;
  targetCombatantId?: string;
  targetX?: number;
  targetY?: number;
}

export interface EncounterStateModel {
  encounterId: string | null;
  combatants: Combatant[];
  active: boolean;
  roundPhase: string | null;
  roundNumber: number;
  myPlannedAction: PlannedAction | null;
  lockedInCombatantIds: string[];
  planningTimerExpiresAt: number | null;
  resolutionSteps: import('../../contracts/hub-contracts.generated').ResolutionStepDto[];
  isPlayingBack: boolean;
}
