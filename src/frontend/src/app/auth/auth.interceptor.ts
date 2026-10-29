import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/** Endpoints that must not carry the bearer header or trigger a refresh. */
const ANONYMOUS_PATHS = ['/api/auth/login', '/api/auth/register', '/api/auth/refresh'];

/**
 * Adds the bearer header to API calls. On a 401 it calls `/refresh` once and retries the request;
 * if the refresh fails the session is cleared and the user is sent to the login page (architecture §3.3).
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!req.url.startsWith('/api/') || ANONYMOUS_PATHS.includes(req.url)) {
    return next(req);
  }

  const withToken = (request: HttpRequest<unknown>) => {
    const token = auth.accessToken;
    return token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;
  };

  return next(withToken(req)).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }

      return auth.refresh().pipe(
        switchMap((refreshed) => {
          if (!refreshed) {
            void router.navigate(['/login']);
            return throwError(() => error);
          }
          return next(withToken(req));
        }),
      );
    }),
  );
};
