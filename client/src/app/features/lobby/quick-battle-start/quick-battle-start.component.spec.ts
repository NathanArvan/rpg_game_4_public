import { ComponentFixture, TestBed } from '@angular/core/testing';
import { QuickBattleStartComponent } from './quick-battle-start.component';
import { QuickBattleService } from '../../../core/services/quick-battle.service';

describe('QuickBattleStartComponent', () => {
  let fixture: ComponentFixture<QuickBattleStartComponent>;
  const quickBattle = jasmine.createSpyObj<QuickBattleService>('QuickBattleService', ['listPresets', 'start']);

  beforeEach(async () => {
    quickBattle.listPresets.and.resolveTo([
      { id: 'skirmish', name: 'Skirmish', description: 'Three vs three' },
      { id: 'training-dummy', name: 'Training Dummy', description: 'One hit' },
    ]);
    quickBattle.start.and.resolveTo();
    await TestBed.configureTestingModule({
      imports: [QuickBattleStartComponent],
      providers: [{ provide: QuickBattleService, useValue: quickBattle }],
    }).compileComponents();

    fixture = TestBed.createComponent(QuickBattleStartComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  it('lists presets with the first one selected', () => {
    const options = fixture.nativeElement.querySelectorAll('option');
    expect(options.length).toBe(2);
    expect(fixture.componentInstance.presetId).toBe('skirmish');
    expect(fixture.nativeElement.querySelector('.description').textContent).toContain('Three vs three');
  });

  it('starts the selected preset', async () => {
    fixture.componentInstance.presetId = 'training-dummy';
    await fixture.componentInstance.start();
    expect(quickBattle.start).toHaveBeenCalledWith('training-dummy');
  });
});
