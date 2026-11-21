import { CommonModule } from '@angular/common';
import { AfterViewChecked, Component, ElementRef, ViewChild } from '@angular/core';
import { Store } from '@ngxs/store';
import { Observable, combineLatest, map, tap } from 'rxjs';
import { FeedState } from '../../../state/feed/feed.state';
import { FeedEntry } from '../../../state/feed/feed.model';
import { MarkFeedRead } from '../../../state/feed/feed.actions';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { FeedLine, NameSides, formatFeedLine } from './feed-format';

interface FeedRow extends FeedLine {
  id: string;
  timestamp: Date;
}

/** How close to the bottom (px) still counts as "at the bottom". */
const BOTTOM_SLACK_PX = 24;

/**
 * The combat log, oldest line first. While you're at the bottom it follows new lines as they
 * arrive; scroll up to read and it stays put, with a "Jump to latest" button to come back.
 */
@Component({
  selector: 'app-feed',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './feed.component.html',
  styleUrl: './feed.component.css',
})
export class FeedComponent implements AfterViewChecked {
  @ViewChild('scroller') private scroller?: ElementRef<HTMLElement>;

  rows$: Observable<FeedRow[]>;
  unreadCount$: Observable<number>;

  /** At the bottom, so new lines scroll into view. False once the reader scrolls up. */
  following = true;
  /** Lines added since the reader scrolled up. */
  newSinceScrolledUp = 0;

  private rowCount = 0;
  private scrollPending = true; // start at the latest line

  constructor(private store: Store) {
    // Names are coloured by side: party (player characters) blue, enemies (monsters) red.
    const sides$ = store.select(EncounterState.combatants).pipe(
      map(combatants => new Map(combatants.map(c =>
        [c.characterName, c.combatantType === 'monster' ? 'enemy' : 'party'] as const)) as NameSides));

    this.rows$ = combineLatest([store.select(FeedState.entries), sides$]).pipe(
      map(([entries, sides]) => entries.map((e: FeedEntry) => ({ id: e.id, timestamp: e.timestamp, ...formatFeedLine(e.text, sides) }))),
      tap(rows => this.onRows(rows.length)));
    this.unreadCount$ = store.select(FeedState.unreadCount);
  }

  ngAfterViewChecked(): void {
    if (this.scrollPending && this.scroller) {
      this.scrollPending = false;
      this.scrollToBottom();
    }
  }

  onScroll(): void {
    this.following = this.isAtBottom();
    if (this.following) this.newSinceScrolledUp = 0;
  }

  jumpToLatest(event: Event): void {
    event.stopPropagation();
    this.following = true;
    this.newSinceScrolledUp = 0;
    this.scrollToBottom();
  }

  trackRow(_: number, row: FeedRow): string {
    return row.id;
  }

  markRead(): void {
    this.store.dispatch(new MarkFeedRead());
  }

  private onRows(count: number): void {
    const added = count - this.rowCount;
    this.rowCount = count;
    if (count === 0) {
      // A cleared log (new battle) starts at the bottom again.
      this.following = true;
      this.newSinceScrolledUp = 0;
    } else if (added > 0) {
      if (this.following) this.scrollPending = true;  // scroll once the new lines are rendered
      else this.newSinceScrolledUp += added;
    }
  }

  private isAtBottom(): boolean {
    const el = this.scroller?.nativeElement;
    return !el || el.scrollHeight - el.scrollTop - el.clientHeight <= BOTTOM_SLACK_PX;
  }

  private scrollToBottom(): void {
    const el = this.scroller?.nativeElement;
    if (el) el.scrollTop = el.scrollHeight;
  }
}
