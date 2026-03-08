import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthResponse } from '../../contracts/api-contracts.generated';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private token: string | null = null;

  constructor(private http: HttpClient) {
    this.token = sessionStorage.getItem('jwt');
  }

  async register(username: string, displayName: string, password: string): Promise<void> {
    const res = await firstValueFrom(
      this.http.post<AuthResponse>('/api/auth/register', { username, displayName, password })
    );
    this.setToken(res.token!);
  }

  async login(username: string, password: string): Promise<void> {
    const res = await firstValueFrom(
      this.http.post<AuthResponse>('/api/auth/token', { username, password })
    );
    this.setToken(res.token!);
  }

  getToken(): string {
    return this.token ?? '';
  }

  isLoggedIn(): boolean {
    return !!this.token;
  }

  getUserId(): string {
    if (!this.token) return '';
    try {
      const payload = JSON.parse(atob(this.token.split('.')[1]));
      return payload.sub ?? payload.nameid ?? '';
    } catch {
      return '';
    }
  }

  logout(): void {
    this.token = null;
    sessionStorage.removeItem('jwt');
  }

  private setToken(token: string): void {
    this.token = token;
    sessionStorage.setItem('jwt', token);
  }
}
