import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideStore, Store } from '@ngxs/store';
import { EncounterResultComponent } from './encounter-result.component';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { FeedState } from '../../../state/feed/feed.state';
import { EncounterEnded } from '../../../state/encounter/encounter.actions';

describe('EncounterResultComponent', () => {
  let component: EncounterResultComponent;
  let fixture: ComponentFixture<EncounterResultComponent>;
  let store: Store;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [EncounterResultComponent],
      providers: [provideStore([EncounterState, FeedState])],
    }).compileComponents();

    fixture = TestBed.createComponent(EncounterResultComponent);
    component = fixture.componentInstance;
    store = TestBed.inject(Store);
    fixture.detectChanges();
  });

  it('renders nothing until the encounter ends', () => {
    expect(fixture.nativeElement.querySelector('.result-card')).toBeNull();
  });

  it('shows Victory to a player when the party wins', () => {
    store.dispatch(new EncounterEnded('party', 3));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.headline').textContent).toContain('Victory');
    expect(fixture.nativeElement.querySelector('.detail').textContent).toContain('round 3');
  });

  it('shows Defeat to a player when the enemies win', () => {
    store.dispatch(new EncounterEnded('enemy', 2));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.headline').textContent).toContain('Defeat');
  });

  it('shows the neutral wording to the GM', () => {
    component.isGm = true;
    store.dispatch(new EncounterEnded('enemy', 2));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.headline').textContent).toContain('Enemies win');
  });

  it('clears the outcome when returning to the lobby', () => {
    store.dispatch(new EncounterEnded('draw', 4));
    fixture.detectChanges();
    fixture.nativeElement.querySelector('.return-btn').click();
    expect(store.selectSnapshot(EncounterState.outcome)).toBeNull();
  });
});
