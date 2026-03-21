import { Injectable } from '@angular/core';
import { CreateCharacterRequest } from '../../contracts/api-contracts.generated';

@Injectable({ providedIn: 'root' })
export class DevService {
  private adjectives = ['swift', 'bold', 'dark', 'iron', 'silver', 'storm', 'frost', 'ember', 'stone', 'wild'];
  private nouns = ['oak', 'wolf', 'hawk', 'river', 'blade', 'forge', 'ash', 'thorn', 'crest', 'vale'];
  private fantasyNames = ['Theron', 'Lyria', 'Corvin', 'Seraphel', 'Daxon', 'Mira', 'Aldric', 'Zara', 'Fenwick', 'Isolde'];
  private races = ['Human', 'Elf', 'Dwarf', 'Halfling', 'Dragonborn', 'Gnome', 'Half-Orc', 'Tiefling'];
  private pick<T>(arr: T[]): T {
    return arr[Math.floor(Math.random() * arr.length)];
  }

  private roll(min: number, max: number): number {
    return Math.floor(Math.random() * (max - min + 1)) + min;
  }

  randomUser(): { username: string; displayName: string; password: string } {
    const suffix = String(this.roll(100, 999));
    return {
      username: this.pick(this.adjectives) + this.pick(this.nouns) + suffix,
      displayName: this.pick(this.fantasyNames),
      password: 'TestPass123!',
    };
  }

  randomCharacter(): CreateCharacterRequest {
    const surname = this.pick(this.nouns);
    return {
      name: this.pick(this.fantasyNames) + ' ' + surname.charAt(0).toUpperCase() + surname.slice(1),
      race: this.pick(this.races),
      level: this.roll(1, 3),
      baseStats: {
        power:      this.roll(8, 18),
        speed:      this.roll(8, 18),
        precision:  this.roll(8, 18),
        resilience: this.roll(8, 18),
        focus:      this.roll(8, 18),
        influence:  this.roll(8, 18),
      },
    };
  }
}
