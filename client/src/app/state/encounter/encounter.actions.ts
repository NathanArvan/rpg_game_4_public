import { Combatant } from './encounter.model';

export class EncounterStarted {
  static readonly type = '[Encounter] Encounter Started';
  constructor(public encounterId: string, public combatants: Combatant[]) {}
}

export class ClearEncounter {
  static readonly type = '[Encounter] Clear';
}
