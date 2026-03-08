import { CharacterSummary } from './character.model';

export class LoadMyCharacters {
  static readonly type = '[Character] Load My Characters';
}

export class SetMyCharacter {
  static readonly type = '[Character] Set My Character';
  constructor(public character: CharacterSummary) {}
}

export class SelectCharacter {
  static readonly type = '[Character] Select Character';
  constructor(public character: CharacterSummary | null) {}
}

export class ClearMyCharacter {
  static readonly type = '[Character] Clear My Character';
}
