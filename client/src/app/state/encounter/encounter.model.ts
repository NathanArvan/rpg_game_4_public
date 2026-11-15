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
  /** Combat profile display name, e.g. "Bruiser" (empty for combatants from before profiles). */
  profileName: string;
  /** Max cells moved per round (shared by all of the round's moves), from the combat profile. */
  movementRange: number;
  /** Action points per round, from the combat profile (3B.2). */
  maxAp: number;
}

/** Movement range for a combatant whose server DTO doesn't carry one (the 3A default). */
export const DEFAULT_MOVEMENT_RANGE = 6;

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
  /** GM-planned queues for monsters this round. */
  enemyPlannedActions: Record<string, PlannedActionEntry[]>;
  /** AP cost per action type ("move", "melee_attack", …), from the server's planning payload. */
  apCosts: Record<string, number>;
  outcome: EncounterOutcome | null;
}
