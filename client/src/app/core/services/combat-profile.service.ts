import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, shareReplay } from 'rxjs';
import { CombatProfileDto } from '../../contracts/api-contracts.generated';

/** Combat profile presets (3B.1) for the character page and the GM's monster picker. */
@Injectable({
  providedIn: 'root'
})
export class CombatProfileService {
  private profiles$?: Observable<CombatProfileDto[]>;

  constructor(private http: HttpClient) { }

  /** All presets, in file order. Fetched once and cached; they only change when the server restarts. */
  getProfiles(): Observable<CombatProfileDto[]> {
    this.profiles$ ??= this.http.get<CombatProfileDto[]>('/api/combat-profiles').pipe(shareReplay(1));
    return this.profiles$;
  }

  /** Presets meant for a side ('party' | 'enemy') first, then the rest. */
  static forSide(profiles: CombatProfileDto[], side: 'party' | 'enemy'): CombatProfileDto[] {
    const rank = (p: CombatProfileDto) => (p.side === side ? 0 : p.side === 'any' ? 1 : 2);
    return [...profiles].sort((a, b) => rank(a) - rank(b));
  }

  /** One-line stat summary, e.g. "24 HP · move 4 · speed 8 · dodge 9 · d8". */
  static summary(p: CombatProfileDto): string {
    return `${p.maxHp} HP · move ${p.movementRange} · speed ${p.speed} · dodge ${p.dodge} · ${p.baseDamage}`;
  }
}
