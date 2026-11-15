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

/**
 * Whether this user plans for the combatant. Checking the type matters in Quick Battle, where the
 * user's ID is also on the enemy rows (the AI plans those).
 */
export function isControlledBy(c: Combatant, userId: string): boolean {
  return c.userId === userId && c.combatantType === 'player';
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
  /** This user's plans for the current round, per combatant they control. */
  myPlannedActions: Record<string, PlannedActionEntry[]>;
  /** Which of this user's combatants they're planning for (several in Quick Battle). */
  selectedCombatantId: string | null;
  lockedInCombatantNames: string[];
  planningTimerExpiresAt: string | null;
  resolutionTicks: TickResult[];
  gmSelectedEnemyId: string | null;
  enemyPlannedActions: Record<string, PlannedActionEntry>;
  outcome: EncounterOutcome | null;
}
