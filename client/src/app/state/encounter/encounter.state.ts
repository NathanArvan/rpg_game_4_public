import { Injectable } from '@angular/core';
import { Action, Selector, State, StateContext } from '@ngxs/store';
import {
  AllLockedIn,
  ArmAbility,
  DisarmAbility,
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
import { Combatant, DEFAULT_MOVEMENT_RANGE, EncounterOutcome, EncounterStateModel, toStatus } from './encounter.model';
import { DEFAULT_AP_COSTS, DEFAULT_MAX_AP } from './plan-queue';
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
    apCosts: { ...DEFAULT_AP_COSTS },
    armedAbilityId: null,
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
  static enemyPlannedActions(state: EncounterStateModel): Record<string, PlannedActionEntry[]> {
    return state.enemyPlannedActions;
  }

  @Selector()
  static apCosts(state: EncounterStateModel): Record<string, number> {
    return state.apCosts;
  }

  @Selector()
  static armedAbilityId(state: EncounterStateModel): string | null {
    return state.armedAbilityId;
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
      status: 'standing' as const,
      bleedoutRoundsLeft: 0,
      combatantType: c.combatantType as 'player' | 'monster',
      isAiControlled: c.isAiControlled ?? false,
      profileName: c.profileName ?? '',
      movementRange: c.movementRange ?? DEFAULT_MOVEMENT_RANGE,
      maxAp: c.maxAp ?? DEFAULT_MAX_AP,
      baseMovementRange: c.movementRange ?? DEFAULT_MOVEMENT_RANGE,
      baseMaxAp: c.maxAp ?? DEFAULT_MAX_AP,
      blocks: [],
      abilities: c.abilities ?? [],
      cooldowns: {},
      conditions: c.conditions ?? [],
    }));
    ctx.patchState({ encounterId, combatants: mapped, active: true });
  }

  @Action(PlanningPhaseStarted)
  planningPhaseStarted(ctx: StateContext<EncounterStateModel>, { roundNumber, planningTimerDurationSeconds, combatants, apCosts }: PlanningPhaseStarted) {
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
      status: toStatus(c.status, c.isDefeated),
      bleedoutRoundsLeft: c.bleedoutRoundsLeft ?? 0,
      combatantType: c.combatantType as 'player' | 'monster',
      isAiControlled: c.isAiControlled ?? false,
      profileName: c.profileName ?? '',
      movementRange: c.movementRange ?? DEFAULT_MOVEMENT_RANGE,
      maxAp: c.maxAp ?? DEFAULT_MAX_AP,
      baseMovementRange: c.baseMovementRange ?? c.movementRange ?? DEFAULT_MOVEMENT_RANGE,
      baseMaxAp: c.baseMaxAp ?? c.maxAp ?? DEFAULT_MAX_AP,
      blocks: c.blocks ?? [],
      abilities: c.abilities ?? [],
      cooldowns: c.cooldowns ?? {},
      conditions: c.conditions ?? [],
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
      apCosts: apCosts ?? { ...DEFAULT_AP_COSTS },
      armedAbilityId: null,
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

    // The server's end-of-round rows carry the statuses (3B.5): downed, stable, dead, rounds left.
    const updated = new Map((payload.updatedCombatants ?? []).map(u => [u.id, u]));
    const combatants: Combatant[] = state.combatants.map(c => {
      const u = updated.get(c.id);
      const isDefeated = u ? u.isDefeated : c.isDefeated || defeatedSet.has(c.id);
      return {
        ...c,
        currentHp: u ? u.currentHp : hpMap.has(c.id) ? hpMap.get(c.id)! : c.currentHp,
        isDefeated,
        status: u ? toStatus(u.status, u.isDefeated) : isDefeated && c.status === 'standing' ? 'dead' : c.status,
        bleedoutRoundsLeft: u ? u.bleedoutRoundsLeft ?? 0 : c.bleedoutRoundsLeft,
        cooldowns: u?.cooldowns ?? c.cooldowns,
        conditions: u?.conditions ?? c.conditions,
      };
    });

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
    ctx.patchState({ selectedCombatantId: combatantId, armedAbilityId: null });
  }

  @Action(ArmAbility)
  armAbility(ctx: StateContext<EncounterStateModel>, { abilityId }: ArmAbility) {
    ctx.patchState({ armedAbilityId: abilityId });
  }

  @Action(DisarmAbility)
  disarmAbility(ctx: StateContext<EncounterStateModel>) {
    ctx.patchState({ armedAbilityId: null });
  }

  @Action(CombatantDefeated)
  combatantDefeated(ctx: StateContext<EncounterStateModel>, { combatantId }: CombatantDefeated) {
    const state = ctx.getState();
    const combatants = state.combatants.map(c =>
      c.id === combatantId ? { ...c, isDefeated: true, status: c.status === 'standing' ? 'dead' as const : c.status } : c
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
      status: toStatus(payload.monster.status, payload.monster.isDefeated),
      bleedoutRoundsLeft: payload.monster.bleedoutRoundsLeft ?? 0,
      combatantType: 'monster',
      isAiControlled: payload.monster.isAiControlled ?? false,
      profileName: payload.monster.profileName ?? '',
      movementRange: payload.monster.movementRange ?? DEFAULT_MOVEMENT_RANGE,
      maxAp: payload.monster.maxAp ?? DEFAULT_MAX_AP,
      baseMovementRange: payload.monster.baseMovementRange ?? payload.monster.movementRange ?? DEFAULT_MOVEMENT_RANGE,
      baseMaxAp: payload.monster.baseMaxAp ?? payload.monster.maxAp ?? DEFAULT_MAX_AP,
      blocks: payload.monster.blocks ?? [],
      abilities: payload.monster.abilities ?? [],
      cooldowns: payload.monster.cooldowns ?? {},
      conditions: payload.monster.conditions ?? [],
    };
    ctx.patchState({ combatants: [...state.combatants, monster] });
  }

  @Action(GmSelectEnemy)
  gmSelectEnemy(ctx: StateContext<EncounterStateModel>, { combatantId }: GmSelectEnemy) {
    ctx.patchState({ gmSelectedEnemyId: combatantId, armedAbilityId: null });
  }

  @Action(GmSetEnemyPlan)
  gmSetEnemyPlan(ctx: StateContext<EncounterStateModel>, { combatantId, actions }: GmSetEnemyPlan) {
    const state = ctx.getState();
    ctx.patchState({
      enemyPlannedActions: { ...state.enemyPlannedActions, [combatantId]: actions },
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
      armedAbilityId: null,
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
      apCosts: { ...DEFAULT_AP_COSTS },
      armedAbilityId: null,
      outcome: null,
    });
  }
}
