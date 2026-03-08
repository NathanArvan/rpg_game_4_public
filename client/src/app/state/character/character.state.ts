import { Injectable } from '@angular/core';
import { Action, Selector, State, StateContext } from '@ngxs/store';
import { tap } from 'rxjs';
import { CharacterService } from '../../core/services/character.service';
import { ClearMyCharacter, LoadMyCharacters, SelectCharacter, SetMyCharacter } from './character.actions';
import { CharacterStateModel, CharacterSummary } from './character.model';

@State<CharacterStateModel>({
  name: 'character',
  defaults: {
    characters: [],
    selected: null,
    loading: false,
  },
})
@Injectable()
export class CharacterState {
  constructor(private characterService: CharacterService) {}

  @Selector()
  static characters(state: CharacterStateModel): CharacterSummary[] {
    return state.characters;
  }

  @Selector()
  static selected(state: CharacterStateModel): CharacterSummary | null {
    return state.selected;
  }

  @Selector()
  static loading(state: CharacterStateModel): boolean {
    return state.loading;
  }

  @Action(LoadMyCharacters)
  loadMyCharacters(ctx: StateContext<CharacterStateModel>) {
    ctx.patchState({ loading: true });
    return this.characterService.getMyCharacters().pipe(
      tap((characters) => ctx.patchState({ characters, loading: false }))
    );
  }

  @Action(SetMyCharacter)
  setMyCharacter(ctx: StateContext<CharacterStateModel>, { character }: SetMyCharacter) {
    const existing = ctx.getState().characters.filter((c) => c.id !== character.id);
    ctx.patchState({ characters: [...existing, character], selected: character });
  }

  @Action(SelectCharacter)
  selectCharacter(ctx: StateContext<CharacterStateModel>, { character }: SelectCharacter) {
    ctx.patchState({ selected: character });
  }

  @Action(ClearMyCharacter)
  clearMyCharacter(ctx: StateContext<CharacterStateModel>) {
    ctx.patchState({ characters: [], selected: null, loading: false });
  }
}
