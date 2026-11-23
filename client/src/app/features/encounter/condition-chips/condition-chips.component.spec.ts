import { ComponentFixture, TestBed } from '@angular/core/testing';
import { makeActiveCondition, makeCondition } from '../../../testing/builders';
import { ConditionChipsComponent } from './condition-chips.component';
import { describeDuration, describeModifiers, conditionTooltip } from './condition-format';

describe('ConditionChipsComponent', () => {
  let fixture: ComponentFixture<ConditionChipsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ConditionChipsComponent] }).compileComponents();
    fixture = TestBed.createComponent(ConditionChipsComponent);
  });

  const chips = () => Array.from(fixture.nativeElement.querySelectorAll('.chip')) as HTMLElement[];

  it('renders nothing without conditions', () => {
    fixture.detectChanges();
    expect(chips().length).toBe(0);
  });

  it('shows each condition with its duration in words, coloured by harm', () => {
    fixture.componentInstance.conditions = [
      makeActiveCondition({ condition: { id: 'marked', name: 'Marked', harmful: true }, roundsLeft: 1 }),
      makeActiveCondition({ condition: { id: 'blessed', name: 'Blessed', harmful: false, modifiers: { accuracy: 2 } }, roundsLeft: 0 }),
    ];
    fixture.detectChanges();

    const [marked, blessed] = chips();
    expect(marked.textContent).toContain('Marked');
    expect(marked.textContent).toContain('next round');
    expect(marked.classList).toContain('harmful');
    expect(blessed.textContent).toContain('this round');
    expect(blessed.classList).toContain('helpful');
    expect(blessed.title).toContain('+2 to hit');
    expect(blessed.title).toContain('until end of round');
  });

  it('compact mode shows only initials', () => {
    fixture.componentInstance.conditions = [makeActiveCondition({ condition: { id: 'marked', name: 'Marked' } })];
    fixture.componentInstance.compact = true;
    fixture.detectChanges();
    expect(chips()[0].textContent!.trim()).toBe('M');
  });
});

describe('condition-format', () => {
  it('never shows a duration number below 2', () => {
    expect(describeDuration(0)).toBe('until end of round');
    expect(describeDuration(1)).toBe('until end of next round');
    expect(describeDuration(3)).toBe('for 3 more rounds');
  });

  it('describes every kind of modifier', () => {
    const m = makeCondition({
      id: 'all', modifiers: {
        accuracy: -3, attackKind: 'ranged', damageDealt: 2, damageTaken: { percent: -50, flat: 2, tags: ['flame'] },
        movement: -2, ap: 1, periodic: { kind: 'damage', die: 'd4', bonus: 1 }, blocks: ['move'],
      },
    }).modifiers;
    expect(describeModifiers(m)).toEqual([
      '-3 to hit (ranged)', '+2 damage per hit', 'takes 2 extra damage per hit from flame', 'takes 50% less damage from flame',
      '-2 movement (from next round)', '+1 AP (from next round)', 'takes d4 + 1 damage each round', "can't move",
    ]);
    expect(describeModifiers(makeCondition({ id: 's', modifiers: { blocks: ['all', 'move'] } }).modifiers)).toEqual(["can't act"]);
  });

  it('a tooltip has the name, what it does and how long', () => {
    const c = makeCondition({ id: 'fortified', name: 'Fortified', description: 'Takes half damage.', modifiers: { damageTaken: { percent: -50, flat: 0, tags: [] } } });
    expect(conditionTooltip(c, 1)).toBe('Fortified\nTakes half damage.\n(takes 50% less damage)\nuntil end of next round');
  });
});
