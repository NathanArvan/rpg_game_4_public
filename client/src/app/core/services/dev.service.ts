import { Injectable } from '@angular/core';
import { CreateCharacterRequest } from '../../contracts/api-contracts.generated';

@Injectable({ providedIn: 'root' })
export class DevService {
  private adjectives = ['swift', 'bold', 'dark', 'iron', 'silver', 'storm', 'frost', 'ember', 'stone', 'wild'];
  private nouns = ['oak', 'wolf', 'hawk', 'river', 'blade', 'forge', 'ash', 'thorn', 'crest', 'vale'];
  private fantasyNames = ['Theron', 'Lyria', 'Corvin', 'Seraphel', 'Daxon', 'Mira', 'Aldric', 'Zara', 'Fenwick', 'Isolde'];
  private races = ['Human', 'Elf', 'Dwarf', 'Halfling', 'Dragonborn', 'Gnome', 'Half-Orc', 'Tiefling'];
  private classes = ['Fighter', 'Wizard', 'Rogue', 'Cleric', 'Paladin', 'Ranger', 'Barbarian', 'Bard'];

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
    const con = this.roll(8, 18);
    const conMod = Math.floor((con - 10) / 2);
    const level = this.roll(1, 3);
    const maxHp = level * 5 + conMod;
    return {
      name: this.pick(this.fantasyNames) + ' ' + this.pick(this.nouns).charAt(0).toUpperCase() + this.pick(this.nouns).slice(1),
      race: this.pick(this.races),
      class: this.pick(this.classes),
      level,
      maxHp,
      currentHp: maxHp,
      baseStats: {
        str: this.roll(8, 18),
        dex: this.roll(8, 18),
        con,
        int: this.roll(8, 18),
        wis: this.roll(8, 18),
        cha: this.roll(8, 18),
      },
    };
  }
}
