import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideStore, Store } from '@ngxs/store';
import { QuickBattlePanelComponent } from './quick-battle-panel.component';
import { GameHubService } from '../../../core/services/game-hub.service';
import { QuickBattleService } from '../../../core/services/quick-battle.service';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { SessionState } from '../../../state/session/session.state';
import { SetQuickBattleSession } from '../../../state/session/session.actions';

describe('QuickBattlePanelComponent', () => {
  let fixture: ComponentFixture<QuickBattlePanelComponent>;
  const quickBattle = jasmine.createSpyObj<QuickBattleService>('QuickBattleService', ['start', 'leave']);

  beforeEach(async () => {
    quickBattle.start.and.resolveTo();
    await TestBed.configureTestingModule({
      imports: [QuickBattlePanelComponent],
      providers: [
        provideStore([EncounterState, SessionState]),
        { provide: GameHubService, useValue: jasmine.createSpyObj('GameHubService', ['forceResolve']) },
        { provide: QuickBattleService, useValue: quickBattle },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(QuickBattlePanelComponent);
    fixture.detectChanges();
  });

  it('is hidden outside a quick battle', () => {
    expect(fixture.nativeElement.querySelector('.quick-battle-panel')).toBeNull();
  });

  it('restarts the same preset', async () => {
    TestBed.inject(Store).dispatch(new SetQuickBattleSession('c', 's', 'QB1234', 'ambush'));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.preset').textContent).toContain('ambush');

    await fixture.componentInstance.restart();
    expect(quickBattle.start).toHaveBeenCalledWith('ambush');
  });
});
