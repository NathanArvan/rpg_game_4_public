export interface Combatant {
  userId: string;
  characterId: string;
  characterName: string;
  initiative: number;
  initiativeRoll: number;
  dexModifier: number;
}

export interface EncounterStateModel {
  encounterId: string | null;
  combatants: Combatant[];
  active: boolean;
}
