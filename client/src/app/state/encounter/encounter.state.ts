import { Injectable } from '@angular/core';
import { Action, Selector, State, StateContext } from '@ngxs/store';
import {
  AllLockedIn,
  ClearEncounter,
  ClearMyPlannedAction,
  CombatantDefeated,
  EncounterEnded,
  EncounterStarted,
  GmClearEnemyPlan,
  GmSelectEnemy,
  GmSetEnemyPlan,
  MonsterAdded,
  PlayerLockedIn,
  PlanningPhaseStarted,
  ResolutionCompleted,
  SelectMyCombatant,
  SetMyPlannedAction,
} from './encounter.actions';
import { Combatant, DEFAULT_MOVEMENT_RANGE, EncounterOutcome, EncounterStateModel } from './encounter.model';
import { CombatantDto, PlannedActionEntry, PlanningCombatantDto, TickResult } from '../../contracts/hub-contracts.generated';
import { MonsterAddedPayload } from '../../contracts/hub-contracts.generated';

@State<EncounterStateModel>({
  name: 'encounter',
  defaults: {
    encounterId: null,
    active: false,
    roundPhase: null,
    roundNumber: 0,
    combatants: [],
    myPlannedActions: {},
    selectedCombatantId: null,
    lockedInCombatantNames: [],
    planningTimerExpiresAt: null,
    resolutionTicks: [],
    gmSelectedEnemyId: null,
    enemyPlannedActions: {},
    outcome: null,
  },
})
@Injectable()
export class EncounterState {
  @Selector()
  static combatants(state: EncounterStateModel): Combatant[] {
    return state.combatants;
  }

  @Selector()
  static active(state: EncounterStateModel): boolean {
    return state.active;
  }

  @Selector()
  static roundPhase(state: EncounterStateModel): string | null {
    return state.roundPhase;
  }

  @Selector()
  static roundNumber(state: EncounterStateModel): number {
    return state.roundNumber;
  }

  @Selector()
  static isPlanning(state: EncounterStateModel): boolean {
    return state.roundPhase === 'planning';
  }

  @Selector()
  static isResolving(state: EncounterStateModel): boolean {
    return state.roundPhase === 'resolving';
  }

  @Selector()
  static myPlannedActions(state: EncounterStateModel): Record<string, PlannedActionEntry[]> {
    return state.myPlannedActions;
  }

  @Selector()
  static selectedCombatantId(state: EncounterStateModel): string | null {
    return state.selectedCombatantId;
  }

  @Selector()
  static lockedInCombatantNames(state: EncounterStateModel): string[] {
    return state.lockedInCombatantNames;
  }

  @Selector()
  static planningTimerExpiresAt(state: EncounterStateModel): string | null {
    return state.planningTimerExpiresAt;
  }

  @Selector()
  static resolutionTicks(state: EncounterStateModel): TickResult[] {
    return state.resolutionTicks;
  }

  @Selector()
  static gmSelectedEnemyId(state: EncounterStateModel): string | null {
    return state.gmSelectedEnemyId;
  }

  @Selector()
  static enemyPlannedActions(state: EncounterStateModel): Record<string, PlannedActionEntry> {
    return state.enemyPlannedActions;
  }

  @Selector()
  static outcome(state: EncounterStateModel): EncounterOutcome | null {
    return state.outcome;
  }

  @Selector()
  static monsterCombatants(state: EncounterStateModel): Combatant[] {
    return state.combatants.filter(c => c.combatantType === 'monster' && !c.isDefeated);
  }

  @Action(EncounterStarted)
  encounterStarted(ctx: StateContext<EncounterStateModel>, { encounterId, combatants }: EncounterStarted) {
    const mapped: Combatant[] = combatants.map((c: CombatantDto) => ({
      ...c,
      isDefeated: false,
      combatantType: c.combatantType as 'player' | 'monster',
      isAiControlled: c.isAiControlled ?? false,
      profileName: c.profileName ?? '',
      movementRange: c.movementRange ?? DEFAULT_MOVEMENT_RANGE,
    }));
    ctx.patchState({ encounterId, combatants: mapped, active: true });
  }

  @Action(PlanningPhaseStarted)
  planningPhaseStarted(ctx: StateContext<EncounterStateModel>, { roundNumber, planningTimerDurationSeconds, combatants }: PlanningPhaseStarted) {
    console.log('[encounter] PlanningPhaseStarted: round=', roundNumber, 'combatants=', combatants.length);
    // 0 seconds means no timer (Quick Battle).
    const expiresAt = planningTimerDurationSeconds > 0
      ? new Date(Date.now() + planningTimerDurationSeconds * 1000).toISOString()
      : null;
    const mapped: Combatant[] = combatants.map((c: PlanningCombatantDto) => ({
      id: c.id,
      userId: c.userId,
      characterId: '',
      characterName: c.characterName,
      currentHp: c.currentHp,
      maxHp: c.maxHp,
      isDefeated: c.isDefeated,
      combatantType: c.combatantType as 'player' | 'monster',
      isAiControlled: c.isAiControlled ?? false,
      profileName: c.profileName ?? '',
      movementRange: c.movementRange ?? DEFAULT_MOVEMENT_RANGE,
    }));
    ctx.patchState({
      roundPhase: 'planning',
      roundNumber,
      combatants: mapped,
      myPlannedActions: {},
      lockedInCombatantNames: [],
      planningTimerExpiresAt: expiresAt,
      resolutionTicks: [],
      gmSelectedEnemyId: null,
      enemyPlannedActions: {},
    });
  }

  @Action(ResolutionCompleted)
  resolutionCompleted(ctx: StateContext<EncounterStateModel>, { payload }: ResolutionCompleted) {
    console.log('[encounter] ResolutionCompleted: ticks=', payload.ticks.length, 'defeated=', payload.defeatedCombatantIds);
    // Update combatant HP and defeated flags from resolution results
    const state = ctx.getState();
    const hpMap = new Map<string, number>();
    const defeatedSet = new Set<string>(payload.defeatedCombatantIds);

    for (const tick of payload.ticks) {
      for (const attack of tick.attacks) {
        if (!attack.wasted) {
          hpMap.set(attack.targetId, attack.targetNewHp);
        }
      }
    }

    const combatants: Combatant[] = state.combatants.map(c => ({
      ...c,
      currentHp: hpMap.has(c.id) ? hpMap.get(c.id)! : c.currentHp,
      isDefeated: c.isDefeated || defeatedSet.has(c.id),
    }));

    ctx.patchState({
      roundPhase: 'resolving',
      resolutionTicks: payload.ticks,
      combatants,
    });
  }

  @Action(PlayerLockedIn)
  playerLockedIn(ctx: StateContext<EncounterStateModel>, { characterName }: PlayerLockedIn) {
    console.log('[encounter] PlayerLockedIn:', characterName);
    const state = ctx.getState();
    ctx.patchState({ lockedInCombatantNames: [...state.lockedInCombatantNames, characterName] });
  }

  @Action(AllLockedIn)
  allLockedIn(ctx: StateContext<EncounterStateModel>) {
    console.log('[encounter] AllLockedIn: transitioning to resolving');
    ctx.patchState({ roundPhase: 'resolving' });
  }

  @Action(SetMyPlannedAction)
  setMyPlannedAction(ctx: StateContext<EncounterStateModel>, { combatantId, actions }: SetMyPlannedAction) {
    ctx.patchState({ myPlannedActions: { ...ctx.getState().myPlannedActions, [combatantId]: actions } });
  }

  @Action(ClearMyPlannedAction)
  clearMyPlannedAction(ctx: StateContext<EncounterStateModel>, { combatantId }: ClearMyPlannedAction) {
    const plans = { ...ctx.getState().myPlannedActions };
    delete plans[combatantId];
    ctx.patchState({ myPlannedActions: plans });
  }

  @Action(SelectMyCombatant)
  selectMyCombatant(ctx: StateContext<EncounterStateModel>, { combatantId }: SelectMyCombatant) {
    ctx.patchState({ selectedCombatantId: combatantId });
  }

  @Action(CombatantDefeated)
  combatantDefeated(ctx: StateContext<EncounterStateModel>, { combatantId }: CombatantDefeated) {
    const state = ctx.getState();
    const combatants = state.combatants.map(c =>
      c.id === combatantId ? { ...c, isDefeated: true } : c
    );
    ctx.patchState({ combatants });
  }

  @Action(MonsterAdded)
  monsterAdded(ctx: StateContext<EncounterStateModel>, { payload }: MonsterAdded) {
    const state = ctx.getState();
    const monster: Combatant = {
      id: payload.monster.id,
      userId: payload.monster.userId,
      characterId: '',
      characterName: payload.monster.characterName,
      currentHp: payload.monster.currentHp,
      maxHp: payload.monster.maxHp,
      isDefeated: payload.monster.isDefeated,
      combatantType: 'monster',
      isAiControlled: payload.monster.isAiControlled ?? false,
      profileName: payload.monster.profileName ?? '',
      movementRange: payload.monster.movementRange ?? DEFAULT_MOVEMENT_RANGE,
    };
    ctx.patchState({ combatants: [...state.combatants, monster] });
  }

  @Action(GmSelectEnemy)
  gmSelectEnemy(ctx: StateContext<EncounterStateModel>, { combatantId }: GmSelectEnemy) {
    ctx.patchState({ gmSelectedEnemyId: combatantId });
  }

  @Action(GmSetEnemyPlan)
  gmSetEnemyPlan(ctx: StateContext<EncounterStateModel>, { combatantId, action }: GmSetEnemyPlan) {
    const state = ctx.getState();
    ctx.patchState({
      enemyPlannedActions: { ...state.enemyPlannedActions, [combatantId]: action },
    });
  }

  @Action(GmClearEnemyPlan)
  gmClearEnemyPlan(ctx: StateContext<EncounterStateModel>, { combatantId }: GmClearEnemyPlan) {
    const state = ctx.getState();
    const updated = { ...state.enemyPlannedActions };
    delete updated[combatantId];
    ctx.patchState({ enemyPlannedActions: updated });
  }

  @Action(EncounterEnded)
  encounterEnded(ctx: StateContext<EncounterStateModel>, { winningSide, finalRoundNumber }: EncounterEnded) {
    console.log('[encounter] EncounterEnded: winner=', winningSide, 'round=', finalRoundNumber);
    ctx.patchState({
      roundPhase: 'ended',
      outcome: { winningSide, finalRoundNumber },
      planningTimerExpiresAt: null,
      myPlannedActions: {},
      gmSelectedEnemyId: null,
      enemyPlannedActions: {},
    });
  }

  @Action(ClearEncounter)
  clearEncounter(ctx: StateContext<EncounterStateModel>) {
    ctx.patchState({
      encounterId: null,
      active: false,
      roundPhase: null,
      roundNumber: 0,
      combatants: [],
      myPlannedActions: {},
      selectedCombatantId: null,
      lockedInCombatantNames: [],
      planningTimerExpiresAt: null,
      resolutionTicks: [],
      gmSelectedEnemyId: null,
      enemyPlannedActions: {},
      outcome: null,
    });
  }
}
