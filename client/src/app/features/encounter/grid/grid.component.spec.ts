import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideStore, Store } from '@ngxs/store';
import { GridComponent } from './grid.component';
import { GameHubService } from '../../../core/services/game-hub.service';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { MapState } from '../../../state/map/map.state';
import { PlanningPhaseStarted, SelectMyCombatant, SetMyPlannedAction } from '../../../state/encounter/encounter.actions';
import { PlanningCombatantDto, AbilityDto } from '../../../contracts/hub-contracts.generated';
import { MapInitialized } from '../../../state/map/map.actions';
import { ResolutionPlaybackService } from '../../../core/services/resolution-playback.service';
import { PLAYBACK_TIMING } from '../../../core/services/playback-timeline';
import { makeAbility, makeAttack, makeMove, makePayload, makePlanningCombatant, makeTick } from '../../../testing/builders';

describe('GridComponent', () => {
  let fixture: ComponentFixture<GridComponent>;
  let grid: GridComponent;
  let store: Store;
  const hub = jasmine.createSpyObj<GameHubService>('GameHubService', ['planAction', 'lockIn', 'forceResolve', 'planEnemyAction']);

  // Quick Battle: the user's ID is on every row, but only the "player" rows are theirs to plan.
  const row = (id: string, name: string, combatantType: string, isDefeated = false, movementRange = 6, maxAp = 4,
    abilities: AbilityDto[] = [], cooldowns: Record<string, number> = {}): PlanningCombatantDto =>
    makePlanningCombatant({ id, characterName: name, isDefeated, combatantType, isAiControlled: combatantType === 'monster',
      movementRange, maxAp, abilities, cooldowns });

  const powerStrike: AbilityDto = makeAbility({ id: 'power_strike', name: 'Power Strike', apCost: 3, cooldown: 2, tags: ['melee'] });

  beforeEach(async () => {
    hub.planAction.calls.reset();
    hub.planAction.and.resolveTo();
    await TestBed.configureTestingModule({
      imports: [GridComponent],
      providers: [provideStore([EncounterState, MapState]), { provide: GameHubService, useValue: hub }],
    }).compileComponents();

    fixture = TestBed.createComponent(GridComponent);
    grid = fixture.componentInstance;
    grid.myUserId = 'me';
    grid.joinCode = 'QB1234';
    store = TestBed.inject(Store);
    fixture.detectChanges();
    store.dispatch(new PlanningPhaseStarted(1, 0, [
      row('bruiser', 'Bruiser', 'player'),
      row('scout', 'Scout', 'player'),
      row('fallen', 'Fallen', 'player', true),
      row('goblin', 'Goblin', 'monster'),
    ]));
  });

  it('counts only living player rows as mine, not the enemy rows that carry my user ID', () => {
    expect(grid.myCombatants.map(c => c.id)).toEqual(['bruiser', 'scout']);
    expect(grid.controlsSeveral).toBeTrue();
  });

  it('plans for the first character until another is selected', () => {
    expect(grid.selected?.id).toBe('bruiser');
    store.dispatch(new SelectMyCombatant('scout'));
    expect(grid.selected?.id).toBe('scout');
  });

  it('sends the plan for the selected character and counts it toward Lock In All', () => {
    store.dispatch(new SelectMyCombatant('scout'));
    grid.planDefend();

    expect(hub.planAction).toHaveBeenCalledWith('QB1234', { actions: [jasmine.objectContaining({ type: 'defend' })] }, 'scout');
    expect(grid.plannedCount).toBe(1);
    expect(grid.lockInLabel).toBe('Lock In All (1/2 planned)');
  });

  it("highlights move cells out to the selected character's own movement range", () => {
    store.dispatch(new PlanningPhaseStarted(1, 0, [
      row('bruiser', 'Bruiser', 'player', false, 4),
      row('scout', 'Scout', 'player', false, 8),
    ]));
    store.dispatch(new MapInitialized('enc', 20, 20, { bruiser: { x: 0, y: 0 }, scout: { x: 0, y: 10 } }));

    expect(grid.isInRange({ x: 4, y: 4 })).toBeTrue();   // bruiser: 4 cells
    expect(grid.isInRange({ x: 5, y: 0 })).toBeFalse();

    store.dispatch(new SelectMyCombatant('scout'));
    expect(grid.isInRange({ x: 8, y: 10 })).toBeTrue();  // scout: 8 cells
    expect(grid.isInRange({ x: 9, y: 10 })).toBeFalse();
  });

  describe('action points (3B.2)', () => {
    beforeEach(() => {
      store.dispatch(new PlanningPhaseStarted(1, 0, [
        row('bruiser', 'Bruiser', 'player', false, 4, 4),
        row('goblin', 'Goblin', 'monster'),
      ]));
      store.dispatch(new MapInitialized('enc', 20, 20, { bruiser: { x: 0, y: 0 }, goblin: { x: 5, y: 0 } }));
    });

    it('appends each click to the queue and sends the whole queue', () => {
      grid.onCellClick({ x: 3, y: 0 });                 // move (1 AP)
      grid.onCellClick({ x: 5, y: 0 });                 // goblin is 2 cells from (3,0): ranged (2 AP)

      expect(grid.selectedPlan.map(a => a.type)).toEqual(['move', 'ranged_attack']);
      expect(hub.planAction).toHaveBeenCalledWith('QB1234', { actions: grid.selectedPlan }, 'bruiser');
      expect(grid.apUsed).toBe(3);
      expect(grid.plannedActionSummary).toBe('1. Move to (3, 0) → 2. Ranged Attack on Goblin');
    });

    it('measures melee reach from where the queued moves end', () => {
      grid.onCellClick({ x: 4, y: 0 });                 // move next to the goblin
      grid.onCellClick({ x: 5, y: 0 });
      expect(grid.selectedPlan[1].type).toBe('melee_attack');
    });

    it('shares the movement range across moves, measured from the last queued move', () => {
      grid.onCellClick({ x: 3, y: 0 });                 // 3 of 4 cells used
      expect(grid.moveCellsLeft).toBe(1);
      expect(grid.isInRange({ x: 3, y: 1 })).toBeTrue();
      expect(grid.isInRange({ x: 3, y: 2 })).toBeFalse();
      expect(grid.isInRange({ x: 1, y: 0 })).toBeFalse(); // back toward the start is 2 cells
    });

    it('ignores actions the AP budget cannot pay for', () => {
      grid.planDefend();                                 // 1
      grid.onCellClick({ x: 5, y: 0 });                  // ranged 2 → 3
      expect(grid.canAffordDefend).toBeTrue();
      grid.onCellClick({ x: 5, y: 0 });                  // another ranged would be 5 > 4
      expect(grid.selectedPlan.length).toBe(2);
      grid.planDefend();                                 // 4
      expect(grid.apUsed).toBe(4);
      expect(grid.canAffordDefend).toBeFalse();
      expect(grid.isInRange({ x: 1, y: 0 })).toBeFalse(); // no AP left to move
    });

    it('undo drops the last action and clear sends an empty queue', () => {
      grid.onCellClick({ x: 2, y: 0 });
      grid.planDefend();
      grid.undoLast();
      expect(grid.selectedPlan.map(a => a.type)).toEqual(['move']);

      grid.clearPlan();
      expect(grid.selectedPlan).toEqual([]);
      expect(hub.planAction).toHaveBeenCalledWith('QB1234', { actions: [] }, 'bruiser');
      expect(grid.plannedCount).toBe(0);
    });

    it('clicking the last queued move target again undoes it', () => {
      grid.onCellClick({ x: 2, y: 0 });
      grid.onCellClick({ x: 2, y: 0 });
      expect(grid.selectedPlan).toEqual([]);
    });
  });

  describe('bleedout (3B.5)', () => {
    const downed = (r: PlanningCombatantDto): PlanningCombatantDto =>
      ({ ...r, currentHp: 0, isDefeated: true, status: 'downed', bleedoutRoundsLeft: 2 });

    beforeEach(() => {
      store.dispatch(new PlanningPhaseStarted(1, 0, [
        row('bruiser', 'Bruiser', 'player'),
        downed(row('scout', 'Scout', 'player')),
        downed(row('goblin', 'Goblin', 'monster')),
      ]));
      store.dispatch(new MapInitialized('enc', 20, 20, { bruiser: { x: 0, y: 0 }, scout: { x: 3, y: 0 }, goblin: { x: 9, y: 9 } }));
    });

    it('clicking a downed ally queues Stabilize only once the queued moves end next to them', () => {
      grid.onCellClick({ x: 3, y: 0 });                 // too far: nothing
      expect(grid.selectedPlan).toEqual([]);

      grid.onCellClick({ x: 2, y: 0 });                 // move next to the scout
      grid.onCellClick({ x: 3, y: 0 });                 // now: stabilize
      expect(grid.selectedPlan.map(a => [a.type, a.targetCombatantId])).toEqual([['move', null], ['stabilize', 'scout']]);
      expect(grid.apUsed).toBe(3);
      expect(grid.plannedActionSummary).toBe('1. Move to (2, 0) → 2. Stabilize Scout');
    });

    it('a downed ally is not selectable for planning, and shows the stabilize hint', () => {
      expect(grid.myCombatants.map(c => c.id)).toEqual(['bruiser']);
      expect(grid.downedAllies.map(c => c.id)).toEqual(['scout']);
    });

    it('clicking a downed enemy plans an attack on it (a finishing blow)', () => {
      grid.onCellClick({ x: 9, y: 9 });
      expect(grid.selectedPlan.map(a => [a.type, a.targetCombatantId])).toEqual([['ranged_attack', 'goblin']]);
    });
  });

  describe('resolution playback', () => {
    const tokenCells = () => Array.from(fixture.nativeElement.querySelectorAll('.token-layer .token') as NodeListOf<HTMLElement>)
      .map(t => `${t.textContent!.trim()[0]}@${t.dataset['x']},${t.dataset['y']}`);

    it('keeps tokens that only attack on the board while the round plays back', fakeAsync(() => {
      store.dispatch(new PlanningPhaseStarted(1, 0, [row('bruiser', 'Bruiser', 'player'), row('goblin', 'Goblin', 'monster')]));
      store.dispatch(new MapInitialized('enc', 20, 20, { bruiser: { x: 0, y: 0 }, goblin: { x: 2, y: 0 } }));
      fixture.detectChanges();

      TestBed.inject(ResolutionPlaybackService).receiveResolution(makePayload({
        ticks: [makeTick({
          movements: [makeMove({ combatantId: 'bruiser', combatantName: 'Bruiser', path: [{ x: 0, y: 0 }, { x: 1, y: 0 }] })],
          attacks: [makeAttack({ attackerId: 'goblin', attackerName: 'Goblin', targetId: 'bruiser', targetName: 'Bruiser',
            hit: true, damage: 2, targetNewHp: 8, feedText: 'Goblin hits Bruiser' })],
        })],
      }));
      fixture.detectChanges();
      expect(tokenCells()).toEqual(['B@1,0', 'G@2,0']);

      tick(PLAYBACK_TIMING.maxStepMs);                 // the attack beat
      fixture.detectChanges();
      expect(tokenCells()).toEqual(['B@1,0', 'G@2,0']);
      expect(fixture.nativeElement.querySelectorAll('.aura-hit').length).toBe(1);
      expect(fixture.nativeElement.querySelectorAll('.aura-hurt').length).toBe(1);
      expect(fixture.nativeElement.querySelector('.float-damage')?.textContent.trim()).toBe('-2');

      tick(PLAYBACK_TIMING.attackMs + PLAYBACK_TIMING.floatMs);
      fixture.detectChanges();
      expect(tokenCells()).toEqual(['B@1,0', 'G@2,0']);  // back on the map's (final) positions
    }));
  });

  describe('abilities (3B.3)', () => {
    beforeEach(() => {
      store.dispatch(new PlanningPhaseStarted(1, 0, [
        row('bruiser', 'Bruiser', 'player', false, 4, 4, [powerStrike]),
        row('scout', 'Scout', 'player'),
        row('goblin', 'Goblin', 'monster'),
      ]));
      store.dispatch(new MapInitialized('enc', 20, 20, { bruiser: { x: 5, y: 5 }, scout: { x: 4, y: 5 }, goblin: { x: 5, y: 6 } }));
      hub.planAction.calls.reset();
    });

    it('arms an ability, shows its reach and legal targets, and queues it on a click', () => {
      grid.toggleAbility('power_strike');
      expect(grid.armedAbility?.id).toBe('power_strike');
      expect(grid.isInAbilityRange({ x: 6, y: 6 })).toBeTrue();
      expect(grid.isInAbilityRange({ x: 7, y: 5 })).toBeFalse();
      expect(grid.isInRange({ x: 6, y: 5 })).toBeFalse(); // move highlight hidden while armed

      const goblin = store.selectSnapshot(EncounterState.combatants).find(c => c.id === 'goblin')!;
      const scout = store.selectSnapshot(EncounterState.combatants).find(c => c.id === 'scout')!;
      expect(grid.isAbilityTarget(goblin)).toBeTrue();
      expect(grid.isAbilityTarget(scout)).toBeFalse(); // an ally

      grid.onCellClick({ x: 5, y: 6 });
      expect(hub.planAction).toHaveBeenCalledWith('QB1234',
        { actions: [jasmine.objectContaining({ type: 'ability', abilityId: 'power_strike', targetCombatantId: 'goblin' })] }, 'bruiser');
      expect(grid.armedAbilityId).toBeNull();
      expect(grid.apUsed).toBe(3);
      expect(grid.plannedActionSummary).toBe('Power Strike on Goblin');
    });

    it('disarms without queueing when the click is not on a legal target', () => {
      grid.toggleAbility('power_strike');
      grid.onCellClick({ x: 4, y: 5 }); // the Scout, an ally
      expect(hub.planAction).not.toHaveBeenCalled();
      expect(grid.armedAbilityId).toBeNull();
    });

    it('disarms on Escape and when switching characters', () => {
      grid.toggleAbility('power_strike');
      grid.onEscape();
      expect(grid.armedAbilityId).toBeNull();

      grid.toggleAbility('power_strike');
      store.dispatch(new SelectMyCombatant('scout'));
      expect(grid.armedAbilityId).toBeNull();
    });
  });

  describe('multi-target & healing (3B.3)', () => {
    const twinShot: AbilityDto = makeAbility({ id: 'twin_shot', name: 'Twin Shot', apCost: 3, cooldown: 1, tags: ['ranged'],
      targeting: { kind: 'combatants', range: 8, count: 2 } });
    const cleave: AbilityDto = makeAbility({ id: 'cleave', name: 'Cleave', apCost: 3, cooldown: 2, tags: ['melee'],
      targeting: { kind: 'auto' } });
    const mend: AbilityDto = makeAbility({ id: 'mend', name: 'Mend', apCost: 2, cooldown: 2, tags: ['healing'],
      targeting: { filter: 'ally', range: 3, includeSelf: true } });

    beforeEach(() => {
      store.dispatch(new PlanningPhaseStarted(1, 0, [
        row('bruiser', 'Bruiser', 'player', false, 4, 5, [twinShot, cleave, mend]),
        row('scout', 'Scout', 'player'),
        row('g1', 'Goblin 1', 'monster'),
        row('g2', 'Goblin 2', 'monster'),
      ]));
      store.dispatch(new MapInitialized('enc', 20, 20,
        { bruiser: { x: 5, y: 5 }, scout: { x: 4, y: 5 }, g1: { x: 5, y: 6 }, g2: { x: 9, y: 9 } }));
      hub.planAction.calls.reset();
    });

    const sent = () => hub.planAction.calls.mostRecent().args[1].actions;

    it('collects distinct targets for a multi-target ability and queues it once the count is reached', () => {
      grid.toggleAbility('twin_shot');
      expect(grid.armedConfirmLabel).toBe('Done (0/2)');
      expect(grid.canConfirmArmed).toBeFalse();

      grid.onCellClick({ x: 9, y: 9 });
      expect(hub.planAction).not.toHaveBeenCalled();
      expect(grid.armedConfirmLabel).toBe('Done (1/2)');
      const g2 = store.selectSnapshot(EncounterState.combatants).find(c => c.id === 'g2')!;
      expect(grid.pickNumber(g2)).toBe(1);

      grid.onCellClick({ x: 4, y: 5 }); // an ally: not a legal pick, stays armed
      expect(grid.armedAbilityId).toBe('twin_shot');

      grid.onCellClick({ x: 5, y: 6 });
      expect(sent()).toEqual([jasmine.objectContaining({ type: 'ability', abilityId: 'twin_shot', targetCombatantIds: ['g2', 'g1'], targetCombatantId: null })]);
      expect(grid.armedAbilityId).toBeNull();
      expect(grid.plannedActionSummary).toBe('Twin Shot on Goblin 2, Goblin 1');
    });

    it('queues fewer targets with Done (or Enter), and a second click drops a pick', () => {
      grid.toggleAbility('twin_shot');
      grid.onCellClick({ x: 9, y: 9 });
      grid.onCellClick({ x: 9, y: 9 }); // dropped again
      expect(grid.pickedTargetIds).toEqual([]);
      grid.onCellClick({ x: 5, y: 6 });
      grid.onEnter();
      expect(sent()).toEqual([jasmine.objectContaining({ abilityId: 'twin_shot', targetCombatantIds: ['g1'] })]);
    });

    it('queues an auto ability with no targets on a grid click, showing its area while armed', () => {
      grid.toggleAbility('cleave');
      expect(grid.armedConfirmLabel).toBe('Use Cleave');
      expect(grid.isInAbilityRange({ x: 6, y: 6 })).toBeTrue();
      expect(grid.isInAbilityRange({ x: 7, y: 7 })).toBeFalse();

      grid.onCellClick({ x: 15, y: 15 });
      expect(sent()).toEqual([jasmine.objectContaining({ type: 'ability', abilityId: 'cleave', targetCombatantId: null, targetCombatantIds: null })]);
      expect(grid.armedAbilityId).toBeNull();
    });

    it('targets an ally (or yourself) with an ally ability instead of switching characters', () => {
      grid.toggleAbility('mend');
      grid.onCellClick({ x: 4, y: 5 }); // the Scout, one of mine
      expect(sent()).toEqual([jasmine.objectContaining({ abilityId: 'mend', targetCombatantId: 'scout' })]);
      expect(grid.selected?.id).toBe('bruiser');

      store.dispatch(new SetMyPlannedAction('bruiser', []));
      grid.toggleAbility('mend');
      grid.onCellClick({ x: 5, y: 5 }); // yourself
      expect(sent()).toEqual([jasmine.objectContaining({ abilityId: 'mend', targetCombatantId: 'bruiser' })]);
    });
  });
  describe('movement abilities (3B.3)', () => {
    const dash: AbilityDto = makeAbility({ id: 'dash', name: 'Dash', apCost: 1, cooldown: 2, tags: ['movement'],
      targeting: { kind: 'self', filter: 'ally', range: 0 }, move: { to: 'cell', maxCells: 3, usesMovementBudget: false } });
    const charge: AbilityDto = makeAbility({ id: 'charge', name: 'Charge', apCost: 3, cooldown: 2, tags: ['melee', 'movement'],
      targeting: { range: 6 }, move: { to: 'target', maxCells: 6, usesMovementBudget: true } });

    beforeEach(() => {
      store.dispatch(new PlanningPhaseStarted(1, 0, [
        row('scout', 'Scout', 'player', false, 4, 5, [dash, charge]),
        row('goblin', 'Goblin', 'monster'),
      ]));
      store.dispatch(new MapInitialized('enc', 20, 20, { scout: { x: 0, y: 5 }, goblin: { x: 6, y: 5 } }));
      hub.planAction.calls.reset();
    });

    const sent = () => hub.planAction.calls.mostRecent().args[1].actions;

    it('highlights Dash cells from where the queued moves end, beyond the movement budget, and queues a click on one', () => {
      grid.onCellClick({ x: 4, y: 5 }); // the whole 4-cell budget
      expect(grid.moveCellsLeft).toBe(0);
      grid.toggleAbility('dash');

      expect(grid.isInRange({ x: 5, y: 5 })).toBeFalse();
      expect(grid.isAbilityMoveTarget({ x: 7, y: 5 })).toBeTrue();
      expect(grid.isAbilityMoveTarget({ x: 8, y: 5 })).toBeFalse();
      expect(grid.isInAbilityRange({ x: 5, y: 5 })).toBeFalse();
      expect(grid.armedConfirmLabel).toBeNull();

      grid.onCellClick({ x: 7, y: 5 });
      expect(sent()).toEqual([
        jasmine.objectContaining({ type: 'move' }),
        jasmine.objectContaining({ type: 'ability', abilityId: 'dash', targetCell: { x: 7, y: 5 } }),
      ]);
      expect(grid.armedAbilityId).toBeNull();
      expect(grid.plannedActionSummary).toBe('1. Move to (4, 5) → 2. Dash to (7, 5)');
      // Later moves start where the Dash ends; the budget is still spent.
      expect(grid.moveCellsLeft).toBe(0);
    });

    it('cancels Dash on a click outside its reach', () => {
      grid.toggleAbility('dash');
      grid.onCellClick({ x: 9, y: 9 });
      expect(hub.planAction).not.toHaveBeenCalled();
      expect(grid.armedAbilityId).toBeNull();
    });

    it('previews the Charge path to a hovered target and counts its cells toward the budget', () => {
      grid.toggleAbility('charge');
      const goblin = grid.tokens.find(t => t.combatant.id === 'goblin')!;
      grid.onTokenHover(goblin);
      // From (0,5) toward (6,5): 5 cells to get next to it, capped at the 4-cell movement budget.
      expect([1, 2, 3, 4].every(x => grid.isChargePath({ x, y: 5 }))).toBeTrue();
      expect(grid.isChargePath({ x: 5, y: 5 })).toBeFalse();

      grid.onCellClick({ x: 6, y: 5 });
      expect(sent()).toEqual([jasmine.objectContaining({ type: 'ability', abilityId: 'charge', targetCombatantId: 'goblin' })]);
      expect(grid.moveCellsLeft).toBe(0);
    });
  });
});
