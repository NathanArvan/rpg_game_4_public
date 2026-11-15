import { PlannedActionEntry, TickResult } from '../../contracts/hub-contracts.generated';

export interface Combatant {
  id: string;
  userId: string;
  characterId: string;
  characterName: string;
  currentHp: number;
  maxHp: number;
  isDefeated: boolean;
  combatantType: 'player' | 'monster';
  /** Planned and locked in by the server's enemy AI each round. */
  isAiControlled: boolean;
}

export interface EncounterOutcome {
  /** 'party' | 'enemy' | 'draw' */
  winningSide: string;
  finalRoundNumber: number;
}

export interface EncounterStateModel {
  encounterId: string | null;
  active: boolean;
  roundPhase: 'planning' | 'resolving' | 'between_rounds' | 'ended' | null;
  roundNumber: number;
  combatants: Combatant[];
  myPlannedActions: PlannedActionEntry[];
  lockedInCombatantNames: string[];
  planningTimerExpiresAt: string | null;
  resolutionTicks: TickResult[];
  gmSelectedEnemyId: string | null;
  enemyPlannedActions: Record<string, PlannedActionEntry>;
  outcome: EncounterOutcome | null;
}
