import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private token: string | null = null;

  constructor(private http: HttpClient) {
    this.token = sessionStorage.getItem('jwt');
  }

  async register(username: string, displayName: string, password: string): Promise<void> {
    const res = await firstValueFrom(
      this.http.post<{ token: string }>('/api/auth/register', {
        username,
        displayName,
        password,
      })
    );
    this.setToken(res.token);
  }

  async login(username: string, password: string): Promise<void> {
    const res = await firstValueFrom(
      this.http.post<{ token: string }>('/api/auth/token', { username, password })
    );
    this.setToken(res.token);
  }

  getToken(): string {
    return this.token ?? '';
  }

  isLoggedIn(): boolean {
    return !!this.token;
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
