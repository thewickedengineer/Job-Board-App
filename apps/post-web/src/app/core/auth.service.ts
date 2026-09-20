import { HttpClient, HttpContext, HttpContextToken } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthResponse, ManagerResponse, TokenPairResponse } from './models';

/** Marks requests the interceptor must leave alone (the auth endpoints themselves). */
export const SKIP_AUTH = new HttpContextToken<boolean>(() => false);

export interface SignupPayload {
  email: string;
  password: string;
  fullName: string;
  organization: string;
}

/**
 * Session state. The access token lives only in this signal (never in
 * storage); the refresh token lives only in the httpOnly cookie the API sets,
 * which the browser attaches when `withCredentials` is on.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/auth`;

  readonly accessToken = signal<string | null>(null);
  readonly manager = signal<ManagerResponse | null>(null);
  readonly isAuthenticated = computed(() => this.accessToken() !== null);

  private refreshing: Promise<boolean> | null = null;

  /** Called once at startup: turn the cookie into a session, if there is one. */
  async restore(): Promise<void> {
    if (await this.refresh()) {
      try {
        const me = await firstValueFrom(this.http.get<ManagerResponse>(`${environment.apiBaseUrl}/api/me`));
        this.manager.set(me);
      } catch {
        this.clear();
      }
    }
  }

  async signup(payload: SignupPayload): Promise<ManagerResponse> {
    const response = await firstValueFrom(
      this.http.post<AuthResponse>(`${this.base}/signup`, payload, this.credentialed()),
    );
    this.accept(response);
    return response.manager;
  }

  async login(email: string, password: string): Promise<ManagerResponse> {
    const response = await firstValueFrom(
      this.http.post<AuthResponse>(`${this.base}/login`, { email, password }, this.credentialed()),
    );
    this.accept(response);
    return response.manager;
  }

  /**
   * Rotates the refresh cookie and replaces the access token. Concurrent
   * callers share one in-flight request so a burst of 401s refreshes once.
   */
  refresh(): Promise<boolean> {
    this.refreshing ??= (async () => {
      try {
        const pair = await firstValueFrom(
          this.http.post<TokenPairResponse>(`${this.base}/refresh`, null, this.credentialed()),
        );
        this.accessToken.set(pair.accessToken);
        return true;
      } catch {
        this.clear();
        return false;
      } finally {
        this.refreshing = null;
      }
    })();
    return this.refreshing;
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post<void>(`${this.base}/logout`, null, this.credentialed()));
    } finally {
      this.clear();
    }
  }

  private accept(response: AuthResponse): void {
    this.accessToken.set(response.accessToken);
    this.manager.set(response.manager);
  }

  private clear(): void {
    this.accessToken.set(null);
    this.manager.set(null);
  }

  private credentialed() {
    return { withCredentials: true, context: new HttpContext().set(SKIP_AUTH, true) };
  }
}
