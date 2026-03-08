export interface BaseStats {
  str: number;
  dex: number;
  con: number;
  int: number;
  wis: number;
  cha: number;
}

export interface CharacterSummary {
  id: string;
  name: string;
  race: string;
  class: string;
  level: number;
  maxHp: number;
  currentHp: number;
  baseStats: BaseStats;
}

export interface CharacterStateModel {
  characters: CharacterSummary[];
  selected: CharacterSummary | null;
  loading: boolean;
}
