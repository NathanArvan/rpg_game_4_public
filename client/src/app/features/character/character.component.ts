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
  baseStats: { power: number; speed: number; precision: number; resilience: number; focus: number; influence: number };
}
import { DevService } from '../../core/services/dev.service';
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
        <label>Race <input [(ngModel)]="form.race" placeholder="flavor only" /></label>
        <label>Class <input [(ngModel)]="form.class" placeholder="flavor only" /></label>
        <label>Level <input type="number" [(ngModel)]="form.level" /></label>
        <h3>Stats</h3>
        <label>Power (melee damage) <input type="number" [(ngModel)]="form.baseStats.power" /></label>
        <label>Speed (move range, dodge) <input type="number" [(ngModel)]="form.baseStats.speed" /></label>
        <label>Precision (attack bonus) <input type="number" [(ngModel)]="form.baseStats.precision" /></label>
        <label>Resilience (max HP) <input type="number" [(ngModel)]="form.baseStats.resilience" /></label>
        <label>Focus (vision, AP) <input type="number" [(ngModel)]="form.baseStats.focus" /></label>
        <label>Influence (team range) <input type="number" [(ngModel)]="form.baseStats.influence" /></label>
        <p class="derived">
          Max HP: <strong>{{ derivedMaxHp }}</strong> &nbsp;
          Move: <strong>{{ derivedMoveRange }}</strong> &nbsp;
          Vision: <strong>{{ derivedVisionRange }}</strong>
        </p>
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
            <span>{{ c.race }} {{ c.class }}</span>
            <span>HP: {{ c.currentHp }}/{{ c.maxHp }} &nbsp; Move: {{ c.movementRange }}</span>
          </div>

          <div *ngIf="!(characters$ | async)?.length" class="empty">No characters yet.</div>

          <button *ngIf="!showCreate" (click)="showCreate = true">+ New Character</button>
          <button *ngIf="!showCreate" (click)="quickCreateCharacter()" class="dev-btn">⚡ Quick Create Random</button>

          <!-- Create Form -->
          <div *ngIf="showCreate" class="create-form">
            <h2>New Character</h2>
            <label>Name <input [(ngModel)]="form.name" /></label>
            <label>Race <input [(ngModel)]="form.race" placeholder="flavor only" /></label>
            <label>Class <input [(ngModel)]="form.class" placeholder="flavor only" /></label>
            <label>Level <input type="number" [(ngModel)]="form.level" /></label>
            <h3>Stats</h3>
            <label>Power (melee damage) <input type="number" [(ngModel)]="form.baseStats.power" /></label>
            <label>Speed (move range, dodge) <input type="number" [(ngModel)]="form.baseStats.speed" /></label>
            <label>Precision (attack bonus) <input type="number" [(ngModel)]="form.baseStats.precision" /></label>
            <label>Resilience (max HP) <input type="number" [(ngModel)]="form.baseStats.resilience" /></label>
            <label>Focus (vision, AP) <input type="number" [(ngModel)]="form.baseStats.focus" /></label>
            <label>Influence (team range) <input type="number" [(ngModel)]="form.baseStats.influence" /></label>
            <p class="derived">
              Max HP: <strong>{{ derivedMaxHp }}</strong> &nbsp;
              Move: <strong>{{ derivedMoveRange }}</strong> &nbsp;
              Vision: <strong>{{ derivedVisionRange }}</strong>
            </p>
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
    .dev-btn { background: #d97706; color: white; border: none; }
    .dev-btn:hover { background: #b45309; }
    .derived { color: #555; font-size: 0.9em; margin: 8px 0; }
  `],
})
export class CharacterComponent implements OnInit {
  characters$: Observable<CharacterSummary[]>;
  selected$: Observable<CharacterSummary | null>;
  loading$: Observable<boolean>;

  showCreate = false;
  formError = '';

  form: CharacterForm = this.emptyForm();

  get derivedMaxHp()      { return 5 + this.form.baseStats.resilience * 2; }
  get derivedMoveRange()  { return 2 + Math.floor(this.form.baseStats.speed / 4); }
  get derivedVisionRange(){ return 2 + Math.floor(this.form.baseStats.focus / 4); }

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
      baseStats: {
        power:     c.baseStats?.power     ?? 10,
        speed:     c.baseStats?.speed     ?? 10,
        precision: c.baseStats?.precision ?? 10,
        resilience: c.baseStats?.resilience ?? 10,
        focus:     c.baseStats?.focus     ?? 10,
        influence: c.baseStats?.influence ?? 10,
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
      baseStats: { power: 10, speed: 10, precision: 10, resilience: 10, focus: 10, influence: 10 },
    };
  }
}
