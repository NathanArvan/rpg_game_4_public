import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideStore, Store } from '@ngxs/store';
import { GridComponent } from './grid.component';
import { GameHubService } from '../../../core/services/game-hub.service';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { MapState } from '../../../state/map/map.state';
import { PlanningPhaseStarted, SelectMyCombatant } from '../../../state/encounter/encounter.actions';
import { PlanningCombatantDto } from '../../../contracts/hub-contracts.generated';
import { MapInitialized } from '../../../state/map/map.actions';

describe('GridComponent', () => {
  let fixture: ComponentFixture<GridComponent>;
  let grid: GridComponent;
  let store: Store;
  const hub = jasmine.createSpyObj<GameHubService>('GameHubService', ['planAction', 'lockIn', 'forceResolve', 'planEnemyAction']);

  // Quick Battle: the user's ID is on every row, but only the "player" rows are theirs to plan.
  const row = (id: string, name: string, combatantType: string, isDefeated = false, movementRange = 6, maxAp = 4): PlanningCombatantDto =>
    ({ id, userId: 'me', characterName: name, currentHp: 10, maxHp: 10, isDefeated, combatantType, isAiControlled: combatantType === 'monster',
       profileId: 'test', profileName: 'Test', movementRange, maxAp });

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
});
