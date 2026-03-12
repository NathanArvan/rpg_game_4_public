import { TestBed } from '@angular/core/testing';
import { provideStore, Store } from '@ngxs/store';
import { MapState } from './map.state';
import { ClearPendingMove, MapInitialized, SetPendingMove, TokenMoved } from './map.actions';

describe('MapState', () => {
  let store: Store;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideStore([MapState])],
    });
    store = TestBed.inject(Store);
  });

  describe('MapInitialized', () => {
    it('sets gridWidth, gridHeight, and positions', () => {
      const positions = { c1: { x: 3, y: 5 }, c2: { x: 10, y: 10 } };
      store.dispatch(new MapInitialized('enc-1', 20, 20, positions));

      const state = store.snapshot().map;
      expect(state.gridWidth).toBe(20);
      expect(state.gridHeight).toBe(20);
      expect(state.positions).toEqual(positions);
    });

    it('clears pendingMove on initialization', () => {
      store.dispatch(new SetPendingMove({ x: 2, y: 3 }));
      store.dispatch(new MapInitialized('enc-1', 20, 20, {}));

      expect(store.selectSnapshot(MapState.pendingMove)).toBeNull();
    });
  });

  describe('TokenMoved', () => {
    it('updates position for the correct combatant', () => {
      store.dispatch(new MapInitialized('enc-1', 20, 20, { c1: { x: 0, y: 0 }, c2: { x: 5, y: 5 } }));
      store.dispatch(new TokenMoved('c1', 3, 4));

      const positions = store.selectSnapshot(MapState.positions);
      expect(positions['c1']).toEqual({ x: 3, y: 4 });
      expect(positions['c2']).toEqual({ x: 5, y: 5 });
    });

    it('clears pendingMove after token moves', () => {
      store.dispatch(new SetPendingMove({ x: 3, y: 4 }));
      store.dispatch(new TokenMoved('c1', 3, 4));

      expect(store.selectSnapshot(MapState.pendingMove)).toBeNull();
    });
  });

  describe('SetPendingMove', () => {
    it('stores the pending position', () => {
      store.dispatch(new SetPendingMove({ x: 7, y: 2 }));

      expect(store.selectSnapshot(MapState.pendingMove)).toEqual({ x: 7, y: 2 });
    });
  });

  describe('ClearPendingMove', () => {
    it('clears the pending position', () => {
      store.dispatch(new SetPendingMove({ x: 7, y: 2 }));
      store.dispatch(new ClearPendingMove());

      expect(store.selectSnapshot(MapState.pendingMove)).toBeNull();
    });
  });
});
