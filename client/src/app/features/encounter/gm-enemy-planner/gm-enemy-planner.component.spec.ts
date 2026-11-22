import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideStore, Store } from '@ngxs/store';
import { PlanningPhaseStarted } from '../../../state/encounter/encounter.actions';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { GameHubService } from '../../../core/services/game-hub.service';
import { GmEnemyPlannerComponent } from './gm-enemy-planner.component';
import { makePlanningCombatant } from '../../../testing/builders';

describe('GmEnemyPlannerComponent', () => {
  let component: GmEnemyPlannerComponent;
  let fixture: ComponentFixture<GmEnemyPlannerComponent>;

  const hubSpy = jasmine.createSpyObj('GameHubService', ['addMonster', 'planEnemyAction', 'lockInEnemies']);

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [GmEnemyPlannerComponent],
      providers: [
        provideStore([EncounterState]),
        { provide: GameHubService, useValue: hubSpy },
      ],
    })
    .compileComponents();

    fixture = TestBed.createComponent(GmEnemyPlannerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('labels AI monsters with an AI badge and manual ones as defending', () => {
    TestBed.inject(Store).dispatch(new PlanningPhaseStarted(1, 300, [
      makePlanningCombatant({ id: 'm1', userId: 'gm', characterName: 'Goblin', combatantType: 'monster', isAiControlled: true }),
      makePlanningCombatant({ id: 'm2', userId: 'gm', characterName: 'Puppet', combatantType: 'monster', isAiControlled: false }),
    ]));
    fixture.detectChanges();

    const rows: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.enemy-row'));
    expect(rows.length).toBe(2);
    expect(rows[0].querySelector('.enemy-plan-badge.ai')?.textContent).toContain('AI');
    expect(rows[1].querySelector('.enemy-plan-badge.empty')?.textContent).toContain('will defend');
  });
});
