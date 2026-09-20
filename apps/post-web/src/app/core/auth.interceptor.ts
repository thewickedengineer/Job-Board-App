import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService, SKIP_AUTH } from './auth.service';

/**
 * Attaches the bearer token to API calls and, on a 401, refreshes once and
 * retries. A second 401 (or a failed refresh) ends the session and sends the
 * user to the login screen with the URL they were on.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (request.context.get(SKIP_AUTH) || !request.url.startsWith(environment.apiBaseUrl)) {
    return next(request);
  }

  return next(withBearer(request, auth.accessToken())).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }

      return from(auth.refresh()).pipe(
        switchMap((refreshed) => {
          if (refreshed) {
            return next(withBearer(request, auth.accessToken()));
          }
          void router.navigate(['/login'], { queryParams: { returnUrl: router.url }, replaceUrl: true });
          return throwError(() => error);
        }),
      );
    }),
  );
};

function withBearer(request: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;
}
