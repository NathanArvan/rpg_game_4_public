import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { Store } from '@ngxs/store';
import { Observable } from 'rxjs';
import { CharacterService, CharacterRequest } from '../../core/services/character.service';
import { LoadMyCharacters, SelectCharacter, SetMyCharacter } from '../../state/character/character.actions';
import { CharacterSummary } from '../../state/character/character.model';
import { CharacterState } from '../../state/character/character.state';

@Component({
  selector: 'app-character',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  template: `
    <div class="character-sheet">
      <h1>My Characters</h1>
      <a routerLink="/">← Back to Lobby</a>

      <div *ngIf="loading$ | async">Loading...</div>

      <!-- Detail / Edit View -->
      <div *ngIf="selected$ | async as selected">
        <h2>Edit Character</h2>
        <label>Name <input [(ngModel)]="form.name" /></label>
        <label>Race <input [(ngModel)]="form.race" /></label>
        <label>Class <input [(ngModel)]="form.class" /></label>
        <label>Level <input type="number" [(ngModel)]="form.level" /></label>
        <label>Max HP <input type="number" [(ngModel)]="form.maxHp" /></label>
        <label>Current HP <input type="number" [(ngModel)]="form.currentHp" /></label>
        <h3>Base Stats</h3>
        <label>STR <input type="number" [(ngModel)]="form.baseStats.str" /></label>
        <label>DEX <input type="number" [(ngModel)]="form.baseStats.dex" /></label>
        <label>CON <input type="number" [(ngModel)]="form.baseStats.con" /></label>
        <label>INT <input type="number" [(ngModel)]="form.baseStats.int" /></label>
        <label>WIS <input type="number" [(ngModel)]="form.baseStats.wis" /></label>
        <label>CHA <input type="number" [(ngModel)]="form.baseStats.cha" /></label>
        <div class="actions">
          <button (click)="saveCharacter(selected.id)">Save</button>
          <button (click)="deleteCharacter(selected.id)" class="danger">Delete</button>
          <button (click)="back()">Back</button>
        </div>
        <p *ngIf="formError" class="error">{{ formError }}</p>
      </div>

      <!-- List View -->
      <div *ngIf="!(selected$ | async)">
        <div *ngIf="!(loading$ | async)">
          <div *ngFor="let c of characters$ | async" class="character-card" (click)="selectCharacter(c)">
            <strong>{{ c.name }}</strong>
            <span>{{ c.race }} {{ c.class }} — Level {{ c.level }}</span>
            <span>HP: {{ c.currentHp }}/{{ c.maxHp }}</span>
          </div>

          <div *ngIf="!(characters$ | async)?.length" class="empty">No characters yet.</div>

          <button *ngIf="!showCreate" (click)="showCreate = true">+ New Character</button>

          <!-- Create Form -->
          <div *ngIf="showCreate" class="create-form">
            <h2>New Character</h2>
            <label>Name <input [(ngModel)]="form.name" /></label>
            <label>Race <input [(ngModel)]="form.race" /></label>
            <label>Class <input [(ngModel)]="form.class" /></label>
            <label>Level <input type="number" [(ngModel)]="form.level" /></label>
            <label>Max HP <input type="number" [(ngModel)]="form.maxHp" /></label>
            <label>Current HP <input type="number" [(ngModel)]="form.currentHp" /></label>
            <h3>Base Stats</h3>
            <label>STR <input type="number" [(ngModel)]="form.baseStats.str" /></label>
            <label>DEX <input type="number" [(ngModel)]="form.baseStats.dex" /></label>
            <label>CON <input type="number" [(ngModel)]="form.baseStats.con" /></label>
            <label>INT <input type="number" [(ngModel)]="form.baseStats.int" /></label>
            <label>WIS <input type="number" [(ngModel)]="form.baseStats.wis" /></label>
            <label>CHA <input type="number" [(ngModel)]="form.baseStats.cha" /></label>
            <div class="actions">
              <button (click)="createCharacter()">Create</button>
              <button (click)="cancelCreate()">Cancel</button>
            </div>
            <p *ngIf="formError" class="error">{{ formError }}</p>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .character-sheet { max-width: 500px; margin: 40px auto; font-family: sans-serif; }
    label { display: block; margin: 6px 0; }
    label input { margin-left: 8px; padding: 4px 8px; }
    input[type=number] { width: 80px; }
    button { margin: 4px 4px 4px 0; padding: 8px 16px; cursor: pointer; }
    button.danger { background: #c00; color: white; }
    .character-card { border: 1px solid #ccc; border-radius: 4px; padding: 12px; margin: 8px 0; cursor: pointer; display: flex; gap: 16px; align-items: center; }
    .character-card:hover { background: #f5f5f5; }
    .character-card strong { flex: 1; }
    .empty { color: #999; margin: 16px 0; }
    .error { color: red; }
    .create-form { margin-top: 16px; padding: 16px; border: 1px solid #ccc; border-radius: 4px; }
    .actions { margin-top: 16px; }
    h3 { margin-bottom: 4px; }
    a { display: inline-block; margin-bottom: 16px; }
  `],
})
export class CharacterComponent implements OnInit {
  characters$: Observable<CharacterSummary[]>;
  selected$: Observable<CharacterSummary | null>;
  loading$: Observable<boolean>;

  showCreate = false;
  formError = '';

  form: CharacterRequest = this.emptyForm();

  constructor(private store: Store, private characterService: CharacterService) {
    this.characters$ = store.select(CharacterState.characters);
    this.selected$ = store.select(CharacterState.selected);
    this.loading$ = store.select(CharacterState.loading);
  }

  ngOnInit(): void {
    this.store.dispatch(new LoadMyCharacters());
  }

  selectCharacter(c: CharacterSummary): void {
    this.form = {
      name: c.name,
      race: c.race,
      class: c.class,
      level: c.level,
      maxHp: c.maxHp,
      currentHp: c.currentHp,
      baseStats: { ...c.baseStats },
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

  saveCharacter(id: string): void {
    this.formError = '';
    this.characterService.updateCharacter(id, this.form).subscribe({
      next: (c) => { this.store.dispatch(new SetMyCharacter(c)); },
      error: (e) => { this.formError = 'Failed to save: ' + (e?.message ?? 'Unknown error'); },
    });
  }

  deleteCharacter(id: string): void {
    this.characterService.deleteCharacter(id).subscribe({
      next: () => {
        this.store.dispatch(new SelectCharacter(null));
        this.store.dispatch(new LoadMyCharacters());
      },
      error: (e) => { this.formError = 'Failed to delete: ' + (e?.message ?? 'Unknown error'); },
    });
  }

  private emptyForm(): CharacterRequest {
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
