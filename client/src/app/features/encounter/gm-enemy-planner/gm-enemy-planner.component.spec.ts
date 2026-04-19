import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideStore } from '@ngxs/store';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { GameHubService } from '../../../core/services/game-hub.service';
import { GmEnemyPlannerComponent } from './gm-enemy-planner.component';

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
});
