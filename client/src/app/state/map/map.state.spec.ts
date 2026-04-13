import { TestBed } from '@angular/core/testing';
import { provideStore, Store } from '@ngxs/store';
import { MapState } from './map.state';
import { ApplyResolutionPositions, MapInitialized } from './map.actions';

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

    it('updates positions on re-initialization', () => {
      store.dispatch(new MapInitialized('enc-1', 20, 20, { c1: { x: 0, y: 0 } }));
      store.dispatch(new MapInitialized('enc-1', 20, 20, { c1: { x: 5, y: 5 } }));

      expect(store.selectSnapshot(MapState.positions)['c1']).toEqual({ x: 5, y: 5 });
    });
  });

  describe('ApplyResolutionPositions', () => {
    it('updates positions map from resolution movement results', () => {
      store.dispatch(new MapInitialized('enc-1', 20, 20, { c1: { x: 0, y: 0 }, c2: { x: 5, y: 5 } }));
      store.dispatch(new ApplyResolutionPositions({ c1: { x: 3, y: 4 }, c2: { x: 5, y: 5 } }));

      const positions = store.selectSnapshot(MapState.positions);
      expect(positions['c1']).toEqual({ x: 3, y: 4 });
      expect(positions['c2']).toEqual({ x: 5, y: 5 });
    });

    it('preserves grid dimensions', () => {
      store.dispatch(new MapInitialized('enc-1', 20, 20, {}));
      store.dispatch(new ApplyResolutionPositions({ c1: { x: 1, y: 1 } }));

      expect(store.selectSnapshot(MapState.gridWidth)).toBe(20);
      expect(store.selectSnapshot(MapState.gridHeight)).toBe(20);
    });
  });

  describe('selectors', () => {
    it('gridWidth returns current width', () => {
      store.dispatch(new MapInitialized('enc-1', 20, 20, {}));
      expect(store.selectSnapshot(MapState.gridWidth)).toBe(20);
    });

    it('positions returns current positions', () => {
      const positions = { c1: { x: 1, y: 2 } };
      store.dispatch(new MapInitialized('enc-1', 20, 20, positions));
      expect(store.selectSnapshot(MapState.positions)).toEqual(positions);
    });
  });
});
