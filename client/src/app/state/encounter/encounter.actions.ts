import { CombatantDto } from '../../contracts/hub-contracts.generated';

export class EncounterStarted {
  static readonly type = '[Encounter] Encounter Started';
  constructor(
    public encounterId: string,
    public combatants: CombatantDto[],
    public activeTurnCombatantId: string
  ) {}
}

export class TurnAdvanced {
  static readonly type = '[Encounter] Turn Advanced';
  constructor(public activeTurnCombatantId: string) {}
}

export class AttackResolved {
  static readonly type = '[Encounter] Attack Resolved';
  constructor(
    public targetCombatantId: string,
    public targetNewHp: number,
    public feedText: string
  ) {}
}

export class CombatantDefeated {
  static readonly type = '[Encounter] Combatant Defeated';
  constructor(public combatantId: string) {}
}

export class ClearEncounter {
  static readonly type = '[Encounter] Clear';
}
