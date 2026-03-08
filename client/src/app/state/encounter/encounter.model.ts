export interface Combatant {
  id: string;
  userId: string;
  characterId: string;
  characterName: string;
  initiative: number;
  initiativeRoll: number;
  dexModifier: number;
  currentHp: number;
  maxHp: number;
  isDefeated: boolean;
}

export interface EncounterStateModel {
  encounterId: string | null;
  combatants: Combatant[];
  active: boolean;
  activeTurnCombatantId: string | null;
}
