import { CharacterResponse } from '../../contracts/api-contracts.generated';

// CharacterSummary is an alias for the generated wire type so existing state code keeps working
export type CharacterSummary = CharacterResponse;

export interface CharacterStateModel {
  characters: CharacterSummary[];
  selected: CharacterSummary | null;
  loading: boolean;
}
