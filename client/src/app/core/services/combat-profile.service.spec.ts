import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { CombatProfileService } from './combat-profile.service';
import { CombatProfileDto } from '../../contracts/api-contracts.generated';

const profile = (id: string, side: string): CombatProfileDto => ({
  id, name: id, description: '', side, maxHp: 10, movementRange: 5, speed: 10,
  meleeAccuracy: 0, rangedAccuracy: 0, dodge: 10, baseDamage: 'd4',
});

describe('CombatProfileService', () => {
  let service: CombatProfileService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(CombatProfileService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('fetches the profiles once and caches them', () => {
    let first: CombatProfileDto[] = [];
    let second: CombatProfileDto[] = [];
    service.getProfiles().subscribe(p => first = p);
    service.getProfiles().subscribe(p => second = p);

    http.expectOne('/api/combat-profiles').flush([profile('grunt', 'enemy')]);

    expect(first.map(p => p.id)).toEqual(['grunt']);
    expect(second).toBe(first);
  });

  it('orders profiles for a side first', () => {
    const sorted = CombatProfileService.forSide(
      [profile('grunt', 'enemy'), profile('any', 'any'), profile('scout', 'party')], 'enemy');
    expect(sorted.map(p => p.id)).toEqual(['grunt', 'any', 'scout']);
  });

  it('summarises a profile', () => {
    expect(CombatProfileService.summary(profile('grunt', 'enemy'))).toBe('10 HP · move 5 · speed 10 · dodge 10 · d4');
  });
});
