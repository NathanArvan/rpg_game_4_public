import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { Store } from '@ngxs/store';
import { Observable } from 'rxjs';
import { FeedState } from '../../../state/feed/feed.state';
import { FeedEntry } from '../../../state/feed/feed.model';
import { MarkFeedRead } from '../../../state/feed/feed.actions';

@Component({
  selector: 'app-feed',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="feed" (click)="markRead()">
      <h3>
        Combat Log
        <span class="unread-badge" *ngIf="(unreadCount$ | async) as count">{{ count }}</span>
      </h3>
      <div class="entries">
        <div *ngFor="let entry of entries$ | async" class="entry">
          <span class="timestamp">{{ entry.timestamp | date:'HH:mm:ss' }}</span>
          {{ entry.text }}
        </div>
        <div *ngIf="!(entries$ | async)?.length" class="empty">No events yet.</div>
      </div>
    </div>
  `,
  styles: [`
    .feed {
      margin-top: 16px;
      padding: 12px 16px;
      background: #1a1a2e;
      color: #e0e0e0;
      border-radius: 4px;
      max-height: 200px;
      overflow-y: auto;
      cursor: pointer;
    }
    h3 { margin: 0 0 8px; font-size: 0.95em; color: #aaa; display: flex; align-items: center; gap: 8px; }
    .unread-badge {
      background: #e53935;
      color: white;
      border-radius: 10px;
      padding: 1px 7px;
      font-size: 0.8em;
    }
    .entry { padding: 3px 0; font-size: 0.9em; border-bottom: 1px solid #333; }
    .timestamp { color: #888; font-size: 0.8em; margin-right: 8px; }
    .empty { color: #555; font-style: italic; font-size: 0.9em; }
  `],
})
export class FeedComponent {
  entries$: Observable<FeedEntry[]>;
  unreadCount$: Observable<number>;

  constructor(private store: Store) {
    this.entries$ = store.select(FeedState.entries);
    this.unreadCount$ = store.select(FeedState.unreadCount);
  }

  markRead(): void {
    this.store.dispatch(new MarkFeedRead());
  }
}
