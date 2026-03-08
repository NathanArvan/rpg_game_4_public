import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { CharacterResponse, CreateCharacterRequest, UpdateCharacterRequest } from '../../contracts/api-contracts.generated';

@Injectable({ providedIn: 'root' })
export class CharacterService {
  constructor(private http: HttpClient) {}

  getMyCharacters(): Observable<CharacterResponse[]> {
    return this.http.get<CharacterResponse[]>('/api/characters/mine');
  }

  createCharacter(req: CreateCharacterRequest): Observable<CharacterResponse> {
    return this.http.post<CharacterResponse>('/api/characters', req);
  }

  updateCharacter(id: string, req: UpdateCharacterRequest): Observable<CharacterResponse> {
    return this.http.put<CharacterResponse>(`/api/characters/${id}`, req);
  }

  deleteCharacter(id: string): Observable<void> {
    return this.http.delete<void>(`/api/characters/${id}`);
  }
}
