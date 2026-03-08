import { Combatant } from './encounter.model';

export class EncounterStarted {
  static readonly type = '[Encounter] Encounter Started';
  constructor(
    public encounterId: string,
    public combatants: Combatant[],
    public activeTurnCombatantId: string
  ) {}
}

export class TurnAdvanced {
  static readonly type = '[Encounter] Turn Advanced';
  constructor(public activeTurnCombatantId: string) {}
}

export class ClearEncounter {
  static readonly type = '[Encounter] Clear';
}
