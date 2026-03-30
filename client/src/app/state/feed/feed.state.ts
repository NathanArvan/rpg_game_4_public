import { Injectable } from '@angular/core';
import { Action, Selector, State, StateContext } from '@ngxs/store';
import { AddFeedBatch, AddFeedEntry, ClearFeed, MarkFeedRead } from './feed.actions';
import { FeedEntry, FeedStateModel } from './feed.model';

@State<FeedStateModel>({
  name: 'feed',
  defaults: {
    entries: [],
    unreadCount: 0,
  },
})
@Injectable()
export class FeedState {
  @Selector()
  static entries(state: FeedStateModel): FeedEntry[] {
    return state.entries;
  }

  @Selector()
  static unreadCount(state: FeedStateModel): number {
    return state.unreadCount;
  }

  @Action(AddFeedEntry)
  addEntry(ctx: StateContext<FeedStateModel>, { text }: AddFeedEntry) {
    const entry: FeedEntry = { id: crypto.randomUUID(), text, timestamp: new Date() };
    const state = ctx.getState();
    ctx.patchState({
      entries: [entry, ...state.entries],
      unreadCount: state.unreadCount + 1,
    });
  }

  @Action(AddFeedBatch)
  addBatch(ctx: StateContext<FeedStateModel>, { texts }: AddFeedBatch) {
    const now = new Date();
    const newEntries: FeedEntry[] = texts.map(text => ({
      id: crypto.randomUUID(),
      text,
      timestamp: now,
    }));
    const state = ctx.getState();
    ctx.patchState({
      entries: [...newEntries, ...state.entries],
      unreadCount: state.unreadCount + newEntries.length,
    });
  }

  @Action(MarkFeedRead)
  markRead(ctx: StateContext<FeedStateModel>) {
    ctx.patchState({ unreadCount: 0 });
  }

  @Action(ClearFeed)
  clearFeed(ctx: StateContext<FeedStateModel>) {
    ctx.patchState({ entries: [], unreadCount: 0 });
  }
}
