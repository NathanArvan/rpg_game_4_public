import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideStore, Store } from '@ngxs/store';
import { GridComponent } from './grid.component';
import { GameHubService } from '../../../core/services/game-hub.service';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { MapState } from '../../../state/map/map.state';
import { PlanningPhaseStarted, SelectMyCombatant } from '../../../state/encounter/encounter.actions';
import { PlanningCombatantDto } from '../../../contracts/hub-contracts.generated';

describe('GridComponent', () => {
  let fixture: ComponentFixture<GridComponent>;
  let grid: GridComponent;
  let store: Store;
  const hub = jasmine.createSpyObj<GameHubService>('GameHubService', ['planAction', 'lockIn', 'forceResolve', 'planEnemyAction']);

  // Quick Battle: the user's ID is on every row, but only the "player" rows are theirs to plan.
  const row = (id: string, name: string, combatantType: string, isDefeated = false): PlanningCombatantDto =>
    ({ id, userId: 'me', characterName: name, currentHp: 10, maxHp: 10, isDefeated, combatantType, isAiControlled: combatantType === 'monster' });

  beforeEach(async () => {
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
});
