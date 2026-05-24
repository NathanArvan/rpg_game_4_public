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
  templateUrl: './feed.component.html',
  styleUrl: './feed.component.css',
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
