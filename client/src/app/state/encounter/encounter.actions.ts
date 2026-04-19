import { CombatantDto, MonsterAddedPayload, PlanningCombatantDto, PlannedActionEntry, ResolutionPayload, TickResult } from '../../contracts/hub-contracts.generated';

export class MonsterAdded {
  static readonly type = '[Encounter] Monster Added';
  constructor(public payload: MonsterAddedPayload) {}
}

export class GmSelectEnemy {
  static readonly type = '[Encounter] GM Select Enemy';
  constructor(public combatantId: string | null) {}
}

export class GmSetEnemyPlan {
  static readonly type = '[Encounter] GM Set Enemy Plan';
  constructor(public combatantId: string, public action: PlannedActionEntry) {}
}

export class GmClearEnemyPlan {
  static readonly type = '[Encounter] GM Clear Enemy Plan';
  constructor(public combatantId: string) {}
}

export class EncounterStarted {
  static readonly type = '[Encounter] Encounter Started';
  constructor(
    public encounterId: string,
    public combatants: CombatantDto[]
  ) {}
}

export class PlanningPhaseStarted {
  static readonly type = '[Encounter] Planning Phase Started';
  constructor(
    public roundNumber: number,
    public planningTimerDurationSeconds: number,
    public combatants: PlanningCombatantDto[]
  ) {}
}

export class PlayerLockedIn {
  static readonly type = '[Encounter] Player Locked In';
  constructor(public characterName: string) {}
}

export class AllLockedIn {
  static readonly type = '[Encounter] All Locked In';
}

export class SetMyPlannedAction {
  static readonly type = '[Encounter] Set My Planned Action';
  constructor(public actions: PlannedActionEntry[]) {}
}

export class ClearMyPlannedAction {
  static readonly type = '[Encounter] Clear My Planned Action';
}

export class CombatantDefeated {
  static readonly type = '[Encounter] Combatant Defeated';
  constructor(public combatantId: string) {}
}

export class ResolutionCompleted {
  static readonly type = '[Encounter] Resolution Completed';
  constructor(public payload: ResolutionPayload) {}
}

export class ClearEncounter {
  static readonly type = '[Encounter] Clear';
}
