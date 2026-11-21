import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { Store } from '@ngxs/store';
import { Observable, combineLatest, map } from 'rxjs';
import { FeedState } from '../../../state/feed/feed.state';
import { FeedEntry } from '../../../state/feed/feed.model';
import { MarkFeedRead } from '../../../state/feed/feed.actions';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { FeedLine, NameSides, formatFeedLine } from './feed-format';

interface FeedRow extends FeedLine {
  id: string;
  timestamp: Date;
}

@Component({
  selector: 'app-feed',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './feed.component.html',
  styleUrl: './feed.component.css',
})
export class FeedComponent {
  rows$: Observable<FeedRow[]>;
  unreadCount$: Observable<number>;

  constructor(private store: Store) {
    // Names are coloured by side: party (player characters) blue, enemies (monsters) red.
    const sides$ = store.select(EncounterState.combatants).pipe(
      map(combatants => new Map(combatants.map(c =>
        [c.characterName, c.combatantType === 'monster' ? 'enemy' : 'party'] as const)) as NameSides));

    this.rows$ = combineLatest([store.select(FeedState.entries), sides$]).pipe(
      map(([entries, sides]) => entries.map((e: FeedEntry) => ({ id: e.id, timestamp: e.timestamp, ...formatFeedLine(e.text, sides) }))));
    this.unreadCount$ = store.select(FeedState.unreadCount);
  }

  trackRow(_: number, row: FeedRow): string {
    return row.id;
  }

  markRead(): void {
    this.store.dispatch(new MarkFeedRead());
  }
}
