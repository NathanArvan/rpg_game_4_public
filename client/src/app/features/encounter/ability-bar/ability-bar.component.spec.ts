import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AbilityBarComponent } from './ability-bar.component';
import { AbilityDto } from '../../../contracts/hub-contracts.generated';
import { Combatant } from '../../../state/encounter/encounter.model';
import { entry } from '../../../state/encounter/plan-queue';
import { makeAbility, makeAbilityCondition, makeCombatant, makePreparedAbility } from '../../../testing/builders';

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

  it('says which conditions an ability applies, in words (3B.4)', () => {
    const mark = makeAbility({
      id: 'mark', name: 'Mark', apCost: 1, cooldown: 2, targeting: { range: 8 },
      appliesConditions: [makeAbilityCondition({ duration: 1 })],
    });
    expect(AbilityBarComponent.tooltip(mark, null)).toContain('Applies marked (takes 2 extra damage per hit) until end of next round');
    const strike = makeAbility({ appliesConditions: [makeAbilityCondition({ duration: 0, requiresHit: true, condition: { id: 'dazzled', name: 'Dazzled', modifiers: { accuracy: -3, attackKind: 'ranged' } } })] });
    expect(AbilityBarComponent.tooltip(strike, null)).toContain('Applies Dazzled (-3 to hit (ranged)) until end of round, on a hit');
  });

  it('marks a prepared reaction with 👁 and its trigger, and greys out the rest once it ends the queue (3B.6)', () => {
    const brace = makePreparedAbility({ description: 'Strike whoever steps in.' });
    component.combatant = makeCombatant({ id: 'h', characterName: 'Hero', abilities: [powerStrike, brace], maxAp: 6 });
    fixture.detectChanges();
    const [strike, watch] = buttons();
    expect(watch.classList).toContain('prepared');
    expect(watch.textContent).toContain('👁');
    expect(watch.title).toContain('👁 Reacts when an enemy steps into reach, until the end of the round. Ends your queue.');

    component.queue = [entry('ability', { abilityId: 'brace' })];
    fixture.detectChanges();
    expect(strike.disabled).toBeTrue();
    expect(strike.title).toContain('(A prepared reaction ends the queue)');
  });
});
