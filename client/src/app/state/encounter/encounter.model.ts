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
}

export interface EncounterStateModel {
  encounterId: string | null;
  active: boolean;
  roundPhase: 'planning' | 'resolving' | 'between_rounds' | null;
  roundNumber: number;
  combatants: Combatant[];
  myPlannedActions: PlannedActionEntry[];
  lockedInCombatantNames: string[];
  planningTimerExpiresAt: string | null;
  resolutionTicks: TickResult[];
}
