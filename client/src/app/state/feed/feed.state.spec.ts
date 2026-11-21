import { TestBed } from '@angular/core/testing';
import { provideStore, Store } from '@ngxs/store';
import { FeedState } from './feed.state';
import { AddFeedBatch, AddFeedEntry, ClearFeed, MarkFeedRead } from './feed.actions';

describe('FeedState', () => {
  let store: Store;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideStore([FeedState])],
    });
    store = TestBed.inject(Store);
  });

  describe('AddFeedEntry', () => {
    it('prepends the new entry to the list', () => {
      store.dispatch(new AddFeedEntry('First event'));
      store.dispatch(new AddFeedEntry('Second event'));

      const entries = store.selectSnapshot(FeedState.entries);
      expect(entries[0].text).toBe('Second event');
      expect(entries[1].text).toBe('First event');
    });

    it('increments unreadCount for each entry added', () => {
      store.dispatch(new AddFeedEntry('Event 1'));
      store.dispatch(new AddFeedEntry('Event 2'));
      store.dispatch(new AddFeedEntry('Event 3'));

      expect(store.selectSnapshot(FeedState.unreadCount)).toBe(3);
    });

    it('assigns a unique id to each entry', () => {
      store.dispatch(new AddFeedEntry('Event 1'));
      store.dispatch(new AddFeedEntry('Event 2'));

      const entries = store.selectSnapshot(FeedState.entries);
      expect(entries[0].id).not.toBe(entries[1].id);
    });

    it('assigns a timestamp to each entry', () => {
      store.dispatch(new AddFeedEntry('Event'));

      const entry = store.selectSnapshot(FeedState.entries)[0];
      expect(entry.timestamp).toBeInstanceOf(Date);
    });
  });

  describe('MarkFeedRead', () => {
    it('resets unreadCount to 0', () => {
      store.dispatch(new AddFeedEntry('Event 1'));
      store.dispatch(new AddFeedEntry('Event 2'));
      store.dispatch(new MarkFeedRead());

      expect(store.selectSnapshot(FeedState.unreadCount)).toBe(0);
    });

    it('does not remove entries when marking read', () => {
      store.dispatch(new AddFeedEntry('Event 1'));
      store.dispatch(new MarkFeedRead());

      expect(store.selectSnapshot(FeedState.entries).length).toBe(1);
    });
  });

  describe('AddFeedBatch', () => {
    it('puts the batch in front of the list, newest (last) line first', () => {
      store.dispatch(new AddFeedEntry('Existing'));
      store.dispatch(new AddFeedBatch(['Round header', 'Attack result']));

      const entries = store.selectSnapshot(FeedState.entries);
      expect(entries.map(e => e.text)).toEqual(['Attack result', 'Round header', 'Existing']);
    });

    it('reads the same whether lines arrive together or one batch at a time', () => {
      store.dispatch(new AddFeedBatch(['A', 'B']));
      store.dispatch(new AddFeedBatch(['C']));

      expect(store.selectSnapshot(FeedState.entries).map(e => e.text)).toEqual(['C', 'B', 'A']);
    });

    it('increments unreadCount by the batch size', () => {
      store.dispatch(new AddFeedBatch(['A', 'B', 'C']));

      expect(store.selectSnapshot(FeedState.unreadCount)).toBe(3);
    });

    it('assigns unique ids to each batch entry', () => {
      store.dispatch(new AddFeedBatch(['A', 'B']));

      const entries = store.selectSnapshot(FeedState.entries);
      expect(entries[0].id).not.toBe(entries[1].id);
    });

    it('does nothing when batch is empty', () => {
      store.dispatch(new AddFeedBatch([]));

      expect(store.selectSnapshot(FeedState.entries).length).toBe(0);
      expect(store.selectSnapshot(FeedState.unreadCount)).toBe(0);
    });
  });

  describe('ClearFeed', () => {
    it('empties the entries list', () => {
      store.dispatch(new AddFeedEntry('Event 1'));
      store.dispatch(new ClearFeed());

      expect(store.selectSnapshot(FeedState.entries)).toEqual([]);
    });

    it('resets unreadCount to 0', () => {
      store.dispatch(new AddFeedEntry('Event 1'));
      store.dispatch(new ClearFeed());

      expect(store.selectSnapshot(FeedState.unreadCount)).toBe(0);
    });
  });
});
