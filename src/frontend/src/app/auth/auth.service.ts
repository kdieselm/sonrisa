import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, finalize, map, of, shareReplay, tap } from 'rxjs';

/** Response of `/api/auth/login` and `/api/auth/refresh` (ASP.NET Core `AccessTokenResponse`). */
interface AccessTokenResponse {
  tokenType: string;
  accessToken: string;
  expiresIn: number;
  refreshToken: string;
}

const REFRESH_TOKEN_KEY = 'sonrisa.refreshToken';
const EMAIL_KEY = 'sonrisa.email';

/**
 * Holds the session (architecture §3.3, D20): the access token only in memory, the refresh token and the
 * account email in `sessionStorage`, so a reload keeps the user logged in and closing the tab ends the session.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly accessTokenSignal = signal<string | null>(null);
  private readonly emailSignal = signal<string | null>(sessionStorage.getItem(EMAIL_KEY));
  private refreshInFlight: Observable<boolean> | null = null;

  readonly isLoggedIn = computed(() => this.accessTokenSignal() !== null);
  readonly email = this.emailSignal.asReadonly();

  get accessToken(): string | null {
    return this.accessTokenSignal();
  }

  register(email: string, password: string): Observable<void> {
    return this.http.post<void>('/api/auth/register', { email, password });
  }

  login(email: string, password: string): Observable<void> {
    return this.http.post<AccessTokenResponse>('/api/auth/login', { email, password }).pipe(
      tap((tokens) => {
        sessionStorage.setItem(EMAIL_KEY, email);
        this.emailSignal.set(email);
        this.storeTokens(tokens);
      }),
      map(() => undefined),
    );
  }

  /** Gets a new access token with the stored refresh token. Emits false (and clears the session) on failure. */
  refresh(): Observable<boolean> {
    const refreshToken = sessionStorage.getItem(REFRESH_TOKEN_KEY);
    if (!refreshToken) {
      return of(false);
    }

    // Parallel 401s share one refresh call.
    this.refreshInFlight ??= this.http
      .post<AccessTokenResponse>('/api/auth/refresh', { refreshToken })
      .pipe(
        tap((tokens) => this.storeTokens(tokens)),
        map(() => true),
        catchError((error: unknown) => {
          // Only a rejected token ends the session. A 5xx or network error (API restarting) keeps the
          // refresh token so the next attempt can still succeed.
          if (error instanceof HttpErrorResponse && (error.status === 400 || error.status === 401)) {
            this.clearSession();
          }
          return of(false);
        }),
        finalize(() => (this.refreshInFlight = null)),
        shareReplay(1),
      );

    return this.refreshInFlight;
  }

  /** Invalidates the refresh token on the server (security stamp, D5), then clears the local session. */
  logout(): Observable<void> {
    return this.http.post<void>('/api/auth/logout', null).pipe(
      catchError(() => of(undefined)),
      finalize(() => this.clearSession()),
      map(() => undefined),
    );
  }

  clearSession(): void {
    this.accessTokenSignal.set(null);
    this.emailSignal.set(null);
    sessionStorage.removeItem(REFRESH_TOKEN_KEY);
    sessionStorage.removeItem(EMAIL_KEY);
  }

  private storeTokens(tokens: AccessTokenResponse): void {
    this.accessTokenSignal.set(tokens.accessToken);
    sessionStorage.setItem(REFRESH_TOKEN_KEY, tokens.refreshToken);
  }
}
