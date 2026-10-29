import { HttpErrorResponse } from '@angular/common/http';

/**
 * Turns an API error into messages for the user. Identity returns `ValidationProblem` (400, `errors` dictionary)
 * for register and `Problem` (401) for a failed login.
 */
export function errorMessages(error: unknown, fallback: string): string[] {
  if (error instanceof HttpErrorResponse) {
    const errors = error.error?.errors as Record<string, string[]> | undefined;
    if (errors) {
      return Object.values(errors).flat();
    }
    if (error.status === 401) {
      return ['Email or password is incorrect.'];
    }
  }
  return [fallback];
}
