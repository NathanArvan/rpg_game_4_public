import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideStore } from '@ngxs/store';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { GameHubService } from '../../../core/services/game-hub.service';
import { CombatSidebarComponent } from './combat-sidebar.component';

describe('CombatSidebarComponent', () => {
  let component: CombatSidebarComponent;
  let fixture: ComponentFixture<CombatSidebarComponent>;

  const hubSpy = jasmine.createSpyObj('GameHubService', ['addMonster', 'planEnemyAction', 'lockInEnemies']);

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CombatSidebarComponent],
      providers: [
        provideStore([EncounterState]),
        { provide: GameHubService, useValue: hubSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CombatSidebarComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
