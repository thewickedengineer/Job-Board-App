import { HttpErrorResponse } from '@angular/common/http';
import { AbstractControl, FormGroup } from '@angular/forms';

/** RFC 9457 problem details as both APIs emit them. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
  retryAfterSeconds?: number;
}

export function asProblem(error: unknown): ProblemDetails | null {
  if (error instanceof HttpErrorResponse && error.error && typeof error.error === 'object') {
    return error.error as ProblemDetails;
  }
  return null;
}

export interface ServerError {
  /** Form control name, or null when the server key matched nothing on the form. */
  control: string | null;
  key: string;
  message: string;
}

/**
 * Applies a ValidationProblemDetails payload onto a typed form exactly as
 * CLAUDE.md §6 prescribes: keys are camelCase control names, so the mapping is
 * mechanical. Each matched control gets `{ server: message }` and clears it on
 * its next value change. Unmatched keys are returned so the banner can still
 * list them — nothing is swallowed.
 */
export function applyServerErrors(form: FormGroup, problem: ProblemDetails): ServerError[] {
  const summary: ServerError[] = [];
  const errors = problem.errors ?? {};

  for (const [key, messages] of Object.entries(errors)) {
    const control = form.get(key);
    const message = messages.join(' ');
    if (control) {
      setServerError(control, message);
      summary.push({ control: key, key, message });
    } else {
      summary.push({ control: null, key, message });
    }
  }

  if (summary.length === 0 && problem.detail) {
    summary.push({ control: null, key: 'detail', message: problem.detail });
  }

  return summary;
}

function setServerError(control: AbstractControl, message: string): void {
  control.setErrors({ ...(control.errors ?? {}), server: message });
  control.markAsTouched();

  // Server errors clear on the next change to that control.
  const sub = control.valueChanges.subscribe(() => {
    sub.unsubscribe();
    if (control.errors && 'server' in control.errors) {
      const { server: _removed, ...rest } = control.errors;
      control.setErrors(Object.keys(rest).length ? rest : null);
    }
  });
}
