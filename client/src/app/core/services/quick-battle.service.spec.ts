import { TestBed } from '@angular/core/testing';
import { provideStore, Store } from '@ngxs/store';
import { QuickBattleService } from './quick-battle.service';
import { GameHubService } from './game-hub.service';
import { SessionState } from '../../state/session/session.state';
import { EncounterState } from '../../state/encounter/encounter.state';
import { FeedState } from '../../state/feed/feed.state';
import { EncounterStarted } from '../../state/encounter/encounter.actions';

describe('QuickBattleService', () => {
  let service: QuickBattleService;
  let store: Store;
  const hub = jasmine.createSpyObj<GameHubService>('GameHubService', ['connect', 'startQuickBattle', 'getQuickBattlePresets']);

  beforeEach(() => {
    hub.connect.and.resolveTo();
    hub.startQuickBattle.and.resolveTo({ joinCode: 'QB1234', campaignId: 'camp', gameSessionId: 'sess', presetId: 'skirmish' });
    TestBed.configureTestingModule({
      providers: [provideStore([SessionState, EncounterState, FeedState]), { provide: GameHubService, useValue: hub }],
    });
    service = TestBed.inject(QuickBattleService);
    store = TestBed.inject(Store);
  });

  it('starts a battle and remembers the session as a player-view quick battle', async () => {
    await service.start('skirmish');

    expect(hub.startQuickBattle).toHaveBeenCalledWith('skirmish');
    const session = store.snapshot().session;
    expect(session.joinCode).toBe('QB1234');
    expect(session.quickBattlePresetId).toBe('skirmish');
    expect(session.isGm).toBeFalse();
  });

  it('leaving clears the encounter and the quick battle session', async () => {
    await service.start('skirmish');
    store.dispatch(new EncounterStarted('enc-1', []));

    service.leave();

    expect(store.snapshot().encounter.active).toBeFalse();
    expect(store.snapshot().session.quickBattlePresetId).toBeNull();
    expect(store.snapshot().session.joinCode).toBeNull();
  });
});
