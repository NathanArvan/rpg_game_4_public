import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideStore, Store } from '@ngxs/store';
import { FeedComponent } from './feed.component';
import { FeedState } from '../../../state/feed/feed.state';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { AddFeedBatch, ClearFeed } from '../../../state/feed/feed.actions';

describe('FeedComponent', () => {
  let fixture: ComponentFixture<FeedComponent>;
  let feed: FeedComponent;
  let store: Store;

  const el = () => fixture.nativeElement as HTMLElement;
  const scroller = () => el().querySelector('.entries') as HTMLElement;
  const jumpButton = () => el().querySelector('.jump-latest') as HTMLButtonElement | null;
  const atBottom = () => scroller().scrollHeight - scroller().scrollTop - scroller().clientHeight <= 1;
  const lines = (from: number, count: number) => Array.from({ length: count }, (_, i) => `Event ${from + i}`);

  /** Adds lines and lets them render (and the log follow them, if it's following). */
  const add = (texts: string[]) => {
    store.dispatch(new AddFeedBatch(texts));
    fixture.detectChanges();
  };

  /** The reader scrolls to the top. */
  const scrollUp = () => {
    scroller().scrollTop = 0;
    feed.onScroll();
    fixture.detectChanges();
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FeedComponent],
      providers: [provideStore([FeedState, EncounterState])],
    }).compileComponents();
    store = TestBed.inject(Store);
    fixture = TestBed.createComponent(FeedComponent);
    feed = fixture.componentInstance;
    el().style.height = '200px'; // a short log, so it scrolls
    fixture.detectChanges();
  });

  it('lists lines oldest first', () => {
    add(['— Battle Start —', '— Round 1 Resolution —', 'Event 1']);
    expect(Array.from(el().querySelectorAll('.entry .text')).map(e => e.textContent)).toEqual(
      ['— Battle Start —', '— Round 1 Resolution —', 'Event 1']);
  });

  it('follows new lines while the reader is at the bottom', () => {
    add(lines(1, 40));
    expect(scroller().scrollHeight).toBeGreaterThan(scroller().clientHeight);
    expect(atBottom()).toBeTrue();

    add(lines(41, 5));
    expect(atBottom()).toBeTrue();
    expect(jumpButton()).toBeNull();
  });

  it('stays put once the reader scrolls up, counting new lines on "Jump to latest"', () => {
    add(lines(1, 40));
    scrollUp();
    expect(jumpButton()!.textContent).toContain('Jump to latest');

    add(lines(41, 3));
    expect(scroller().scrollTop).toBe(0);
    expect(jumpButton()!.textContent).toContain('(3 new)');

    jumpButton()!.click();
    fixture.detectChanges();
    expect(atBottom()).toBeTrue();
    expect(jumpButton()).toBeNull();

    add(lines(44, 3));                       // following again
    expect(atBottom()).toBeTrue();
  });

  it('starts following again when the log is cleared for a new battle', () => {
    add(lines(1, 40));
    scrollUp();
    store.dispatch(new ClearFeed());
    fixture.detectChanges();
    expect(jumpButton()).toBeNull();

    add(lines(1, 40));
    expect(atBottom()).toBeTrue();
  });
});
