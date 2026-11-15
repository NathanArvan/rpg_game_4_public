import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideStore } from '@ngxs/store';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { MapState } from '../../../state/map/map.state';
import { FeedState } from '../../../state/feed/feed.state';
import { GameHubService } from '../../../core/services/game-hub.service';
import { AuthService } from '../../../core/services/auth.service';
import { QuickBattleService } from '../../../core/services/quick-battle.service';
import { SessionState } from '../../../state/session/session.state';
import { CombatLayoutComponent } from './combat-layout.component';

describe('CombatLayoutComponent', () => {
  let component: CombatLayoutComponent;
  let fixture: ComponentFixture<CombatLayoutComponent>;

  const hubSpy = jasmine.createSpyObj('GameHubService', [
    'addMonster', 'planEnemyAction', 'lockInEnemies',
    'planAction', 'lockIn', 'forceResolve',
  ]);

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CombatLayoutComponent],
      providers: [
        provideStore([EncounterState, MapState, FeedState, SessionState]),
        { provide: GameHubService, useValue: hubSpy },
        { provide: AuthService, useValue: { getUserId: () => 'user-1' } },
        { provide: QuickBattleService, useValue: jasmine.createSpyObj('QuickBattleService', ['start', 'leave']) },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CombatLayoutComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
