import { CombatantDto } from '../../contracts/hub-contracts.generated';
import { PlannedAction } from './encounter.model';

export class EncounterStarted {
  static readonly type = '[Encounter] Encounter Started';
  constructor(
    public encounterId: string,
    public combatants: CombatantDto[],
    public roundNumber: number,
    public roundPhase: string,
    public planningTimerExpiresAt: number
  ) {}
}

export class ActionPlanned {
  static readonly type = '[Encounter] Action Planned';
  constructor(public action: PlannedAction) {}
}

export class ClearPlannedAction {
  static readonly type = '[Encounter] Clear Planned Action';
}

export class PlayerLockedIn {
  static readonly type = '[Encounter] Player Locked In';
  constructor(public combatantId: string) {}
}

export class AllLockedIn {
  static readonly type = '[Encounter] All Locked In';
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
