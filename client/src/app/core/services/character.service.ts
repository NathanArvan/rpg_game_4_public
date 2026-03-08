import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { CharacterSummary } from '../../state/character/character.model';

export interface CharacterRequest {
  name: string;
  race: string;
  class: string;
  level: number;
  baseStats: { str: number; dex: number; con: number; int: number; wis: number; cha: number };
  maxHp: number;
  currentHp: number;
}

@Injectable({ providedIn: 'root' })
export class CharacterService {
  constructor(private http: HttpClient) {}

  getMyCharacters(): Observable<CharacterSummary[]> {
    return this.http.get<CharacterSummary[]>('/api/characters/mine');
  }

  createCharacter(req: CharacterRequest): Observable<CharacterSummary> {
    return this.http.post<CharacterSummary>('/api/characters', req);
  }

  updateCharacter(id: string, req: CharacterRequest): Observable<CharacterSummary> {
    return this.http.put<CharacterSummary>(`/api/characters/${id}`, req);
  }

  deleteCharacter(id: string): Observable<void> {
    return this.http.delete<void>(`/api/characters/${id}`);
  }
}
