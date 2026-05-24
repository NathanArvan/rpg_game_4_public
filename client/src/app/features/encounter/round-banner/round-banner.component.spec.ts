import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideStore } from '@ngxs/store';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { RoundBannerComponent } from './round-banner.component';

describe('RoundBannerComponent', () => {
  let component: RoundBannerComponent;
  let fixture: ComponentFixture<RoundBannerComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RoundBannerComponent],
      providers: [provideStore([EncounterState])],
    }).compileComponents();

    fixture = TestBed.createComponent(RoundBannerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
