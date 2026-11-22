import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AbilityBarComponent } from './ability-bar.component';
import { AbilityDto } from '../../../contracts/hub-contracts.generated';
import { Combatant } from '../../../state/encounter/encounter.model';
import { entry } from '../../../state/encounter/plan-queue';
import { makeAbility, makeCombatant } from '../../../testing/builders';

describe('AbilityBarComponent', () => {
  let component: AbilityBarComponent;
  let fixture: ComponentFixture<AbilityBarComponent>;

  const powerStrike: AbilityDto = makeAbility({
    id: 'power_strike', name: 'Power Strike', description: 'A heavy blow.', apCost: 3, cooldown: 2, tags: ['melee'],
  });
  const firebolt: AbilityDto = makeAbility({
    id: 'firebolt', name: 'Firebolt', description: 'A heavy blow.', apCost: 3, cooldown: 1, tags: ['melee'], targeting: { range: 8 },
  });
  const hero = (cooldowns: Record<string, number> = {}): Combatant =>
    makeCombatant({ id: 'h', characterName: 'Hero', abilities: [powerStrike, firebolt], cooldowns });

  const buttons = () => Array.from(fixture.nativeElement.querySelectorAll('.ability-btn')) as HTMLButtonElement[];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AbilityBarComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(AbilityBarComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('shows nothing without a combatant', () => {
    expect(buttons().length).toBe(0);
  });

  it('shows one button per ability, with its AP cost and any cooldown', () => {
    component.combatant = hero({ firebolt: 1 });
    fixture.detectChanges();

    const [strike, bolt] = buttons();
    expect(strike.textContent).toContain('Power Strike');
    expect(strike.textContent).toContain('3 AP');
    expect(strike.disabled).toBeFalse();
    expect(bolt.textContent).toContain('⏳ 1');
    expect(bolt.disabled).toBeTrue();
    expect(bolt.title).toContain('On cooldown for 1 more round');
  });

  it('disables an ability once queued or unaffordable', () => {
    component.combatant = hero();
    component.queue = [entry('ability', { abilityId: 'power_strike', targetCombatantId: 'g' })];
    fixture.detectChanges();

    const [strike, bolt] = buttons();
    expect(strike.title).toContain('Already queued this round');
    expect(bolt.title).toContain('Not enough AP left'); // 3 + 3 > 4
    expect(strike.disabled && bolt.disabled).toBeTrue();
  });

  it('emits the ability to arm, and marks the armed one', () => {
    component.combatant = hero();
    const toggled: string[] = [];
    component.toggle.subscribe((id: string) => toggled.push(id));
    fixture.detectChanges();

    buttons()[0].click();
    expect(toggled).toEqual(['power_strike']);

    component.armedAbilityId = 'power_strike';
    fixture.detectChanges();
    expect(buttons()[0].classList).toContain('armed');
  });

  it('describes the ability in its tooltip', () => {
    expect(AbilityBarComponent.tooltip(firebolt, null))
      .toBe('Firebolt — 3 AP · cooldown 1 round\nA heavy blow.\nTarget: an enemy within 8 cells');
  });
});
