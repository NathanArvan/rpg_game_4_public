import { TestBed } from '@angular/core/testing';
import { provideStore, Store } from '@ngxs/store';
import { SessionState } from './session.state';
import { PlayerPresenceChanged, SetGmSession } from './session.actions';
import { ConnectedPlayer } from './session.model';

const makePlayer = (overrides: Partial<ConnectedPlayer> = {}): ConnectedPlayer => ({
  userId: 'u1',
  displayName: 'Frodo',
  characterId: 'ch1',
  isOnline: true,
  ...overrides,
});

describe('SessionState', () => {
  let store: Store;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideStore([SessionState])],
    });
    store = TestBed.inject(Store);
  });

  describe('PlayerPresenceChanged', () => {
    it('adds a new online player', () => {
      store.dispatch(new PlayerPresenceChanged(makePlayer({ userId: 'u1', isOnline: true })));

      const players = store.selectSnapshot(SessionState.connectedPlayers);
      expect(players.length).toBe(1);
      expect(players[0].userId).toBe('u1');
    });

    it('updates an existing player when they reconnect', () => {
      store.dispatch(new PlayerPresenceChanged(makePlayer({ userId: 'u1', displayName: 'Frodo' })));
      store.dispatch(new PlayerPresenceChanged(makePlayer({ userId: 'u1', displayName: 'Frodo Baggins' })));

      const players = store.selectSnapshot(SessionState.connectedPlayers);
      expect(players.length).toBe(1);
      expect(players[0].displayName).toBe('Frodo Baggins');
    });

    it('removes a player when they go offline', () => {
      store.dispatch(new PlayerPresenceChanged(makePlayer({ userId: 'u1', isOnline: true })));
      store.dispatch(new PlayerPresenceChanged(makePlayer({ userId: 'u1', isOnline: false })));

      const players = store.selectSnapshot(SessionState.connectedPlayers);
      expect(players.length).toBe(0);
    });

    it('keeps other players when one goes offline', () => {
      store.dispatch(new PlayerPresenceChanged(makePlayer({ userId: 'u1', isOnline: true })));
      store.dispatch(new PlayerPresenceChanged(makePlayer({ userId: 'u2', displayName: 'Samwise', isOnline: true })));
      store.dispatch(new PlayerPresenceChanged(makePlayer({ userId: 'u1', isOnline: false })));

      const players = store.selectSnapshot(SessionState.connectedPlayers);
      expect(players.length).toBe(1);
      expect(players[0].userId).toBe('u2');
    });

    it('sets status to connected', () => {
      store.dispatch(new PlayerPresenceChanged(makePlayer()));

      expect(store.selectSnapshot(SessionState.status)).toBe('connected');
    });
  });

  describe('SetGmSession', () => {
    it('sets isGm, campaignId, gameSessionId, and joinCode', () => {
      store.dispatch(new SetGmSession('camp-1', 'sess-1', 'ABC123'));

      const state = store.snapshot().session;
      expect(state.isGm).toBeTrue();
      expect(state.campaignId).toBe('camp-1');
      expect(state.gameSessionId).toBe('sess-1');
      expect(state.joinCode).toBe('ABC123');
    });
  });
});
