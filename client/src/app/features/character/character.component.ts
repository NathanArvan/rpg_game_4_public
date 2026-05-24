import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { Store } from '@ngxs/store';
import { Observable } from 'rxjs';
import { CharacterService } from '../../core/services/character.service';
import { CreateCharacterRequest } from '../../contracts/api-contracts.generated';

interface CharacterForm {
  name: string; race: string; class: string; level: number;
  maxHp: number; currentHp: number;
  baseStats: { str: number; dex: number; con: number; int: number; wis: number; cha: number };
}
import { DevService } from '../../core/services/dev.service';
import { LoadMyCharacters, SelectCharacter, SetMyCharacter } from '../../state/character/character.actions';
import { CharacterSummary } from '../../state/character/character.model';
import { CharacterState } from '../../state/character/character.state';

@Component({
  selector: 'app-character',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './character.component.html',
  styleUrl: './character.component.css',
})
export class CharacterComponent implements OnInit {
  characters$: Observable<CharacterSummary[]>;
  selected$: Observable<CharacterSummary | null>;
  loading$: Observable<boolean>;

  showCreate = false;
  formError = '';

  form: CharacterForm = this.emptyForm();

  constructor(private store: Store, private characterService: CharacterService, private dev: DevService) {
    this.characters$ = store.select(CharacterState.characters);
    this.selected$ = store.select(CharacterState.selected);
    this.loading$ = store.select(CharacterState.loading);
  }

  ngOnInit(): void {
    this.store.dispatch(new LoadMyCharacters());
  }

  selectCharacter(c: CharacterSummary): void {
    this.form = {
      name: c.name ?? '',
      race: c.race ?? '',
      class: c.class ?? '',
      level: c.level ?? 1,
      maxHp: c.maxHp ?? 10,
      currentHp: c.currentHp ?? 10,
      baseStats: {
        str: c.baseStats?.str ?? 10, dex: c.baseStats?.dex ?? 10, con: c.baseStats?.con ?? 10,
        int: c.baseStats?.int ?? 10, wis: c.baseStats?.wis ?? 10, cha: c.baseStats?.cha ?? 10,
      },
    };
    this.formError = '';
    this.store.dispatch(new SelectCharacter(c));
  }

  back(): void {
    this.store.dispatch(new SelectCharacter(null));
  }

  cancelCreate(): void {
    this.showCreate = false;
    this.form = this.emptyForm();
    this.formError = '';
  }

  quickCreateCharacter(): void {
    this.formError = '';
    this.characterService.createCharacter(this.dev.randomCharacter()).subscribe({
      next: (c) => {
        this.store.dispatch(new SetMyCharacter(c));
        this.store.dispatch(new LoadMyCharacters());
      },
      error: (e) => { this.formError = 'Quick create failed: ' + (e?.message ?? 'Unknown error'); },
    });
  }

  createCharacter(): void {
    this.formError = '';
    this.characterService.createCharacter(this.form).subscribe({
      next: (c) => {
        this.store.dispatch(new SetMyCharacter(c));
        this.showCreate = false;
        this.form = this.emptyForm();
      },
      error: (e) => { this.formError = 'Failed to create: ' + (e?.message ?? 'Unknown error'); },
    });
  }

  saveCharacter(id: string | undefined): void {
    if (!id) return;
    this.formError = '';
    this.characterService.updateCharacter(id, this.form).subscribe({
      next: (c) => { this.store.dispatch(new SetMyCharacter(c)); },
      error: (e) => { this.formError = 'Failed to save: ' + (e?.message ?? 'Unknown error'); },
    });
  }

  deleteCharacter(id: string | undefined): void {
    if (!id) return;
    this.characterService.deleteCharacter(id).subscribe({
      next: () => {
        this.store.dispatch(new SelectCharacter(null));
        this.store.dispatch(new LoadMyCharacters());
      },
      error: (e) => { this.formError = 'Failed to delete: ' + (e?.message ?? 'Unknown error'); },
    });
  }

  private emptyForm(): CharacterForm {
    return {
      name: '',
      race: '',
      class: '',
      level: 1,
      maxHp: 10,
      currentHp: 10,
      baseStats: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 },
    };
  }
}
