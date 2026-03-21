import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideStore } from '@ngxs/store';
import { GmEnemyPanelComponent } from './gm-enemy-panel.component';
import { EncounterState } from '../../../state/encounter/encounter.state';
import { GameHubService } from '../../../core/services/game-hub.service';

describe('GmEnemyPanelComponent', () => {
  let component: GmEnemyPanelComponent;
  let fixture: ComponentFixture<GmEnemyPanelComponent>;

  const hubStub = { planEnemyAction: jasmine.createSpy(), lockInEnemies: jasmine.createSpy() };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [GmEnemyPanelComponent],
      providers: [
        provideStore([EncounterState]),
        { provide: GameHubService, useValue: hubStub },
      ],
    })
    .compileComponents();

    fixture = TestBed.createComponent(GmEnemyPanelComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
