import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, Injector, computed, inject, signal, viewChild } from '@angular/core';
import { focusAfterRender } from '../../core/focus';
import { toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { debounceTime, map } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { applyServerErrors, asProblem } from '../../core/problem-details';
import { ToastService } from '../../core/toast.service';
import { ErrorSummaryComponent, SummaryEntry, bannerOf } from '../../shared/error-summary.component';
import { AuthLayoutComponent } from './auth-layout.component';

const FIELD_LABELS: Record<string, string> = {
  email: 'Work email',
  fullName: 'Full name',
  organization: 'Company / organization',
  password: 'Password',
  confirmPassword: 'Confirm password',
};

function passwordsMatch(group: AbstractControl): ValidationErrors | null {
  const password = group.get('password')?.value;
  const confirm = group.get('confirmPassword')?.value;
  return password && confirm && password !== confirm ? { passwordMismatch: true } : null;
}

/** Client-derived only, never sent (wireframe 1.1). */
function strengthOf(password: string): { label: string; score: number } {
  let score = 0;
  if (password.length >= 12) score++;
  if (/[a-z]/.test(password) && /[A-Z]/.test(password)) score++;
  if (/\d/.test(password)) score++;
  if (/[^A-Za-z0-9]/.test(password)) score++;
  const label = password.length === 0 ? '' : score <= 1 ? 'Weak' : score === 2 ? 'Fair' : score === 3 ? 'Strong' : 'Very strong';
  return { label, score };
}

/**
 * Wireframe 1.1: validation on blur, re-validate on keystroke once errored,
 * strength meter (role=status, polite, debounced 500 ms), submit disabled
 * until required fields are non-empty and terms are checked, server errors
 * mapped onto fields with a banner, success → dashboard with a toast.
 */
@Component({
  selector: 'app-signup',
  imports: [ReactiveFormsModule, AuthLayoutComponent, ErrorSummaryComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-auth-layout asideText="Already have an account?" asideLink="/login" asideLabel="Log in">
      <app-error-summary #summary [entries]="serverErrors()" [title]="summaryTitle()" />
      <h1 class="title">Create your hiring account</h1>

      <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="stack" [attr.aria-busy]="submitting()">
        <div class="field">
          <label class="label" for="email">Work email <i class="req" aria-hidden="true">*</i></label>
          <input id="email" class="input" type="email" formControlName="email" autocomplete="email" placeholder="dana.whitfield@northline.co"
                 [readonly]="submitting()" [attr.aria-invalid]="showError('email') ? 'true' : null" aria-describedby="email-hint email-error" />
          <p id="email-hint" class="hint">Personal domains (gmail, outlook) are rejected server-side.</p>
          @if (showError('email')) { <p id="email-error" class="field-error">{{ errorFor('email') }}</p> }
        </div>

        <div class="field">
          <label class="label" for="fullName">Full name <i class="req" aria-hidden="true">*</i></label>
          <input id="fullName" class="input" formControlName="fullName" autocomplete="name" [readonly]="submitting()"
                 [attr.aria-invalid]="showError('fullName') ? 'true' : null" aria-describedby="fullName-error" />
          @if (showError('fullName')) { <p id="fullName-error" class="field-error">{{ errorFor('fullName') }}</p> }
        </div>

        <div class="field">
          <label class="label" for="organization">Company / organization <i class="req" aria-hidden="true">*</i></label>
          <input id="organization" class="input" formControlName="organization" autocomplete="organization" [readonly]="submitting()"
                 [attr.aria-invalid]="showError('organization') ? 'true' : null" aria-describedby="organization-error" />
          @if (showError('organization')) { <p id="organization-error" class="field-error">{{ errorFor('organization') }}</p> }
        </div>

        <div class="field">
          <label class="label" for="password">Password <i class="req" aria-hidden="true">*</i></label>
          <div class="input-group">
            <input id="password" class="input" [type]="showPassword() ? 'text' : 'password'" formControlName="password" autocomplete="new-password"
                   [readonly]="submitting()" [attr.aria-invalid]="showError('password') ? 'true' : null" aria-describedby="password-strength password-error" />
            <button type="button" class="btn btn-ghost btn-sm input-addon" (click)="showPassword.set(!showPassword())" [attr.aria-pressed]="showPassword()">
              {{ showPassword() ? 'Hide' : 'Show' }}
            </button>
          </div>
          <p id="password-strength" class="hint" role="status" aria-live="polite">
            @if (strength().label) { Strength: {{ strength().label }} · } 12+ characters, mix of cases and a number
          </p>
          @if (showError('password')) { <p id="password-error" class="field-error">{{ errorFor('password') }}</p> }
        </div>

        <div class="field">
          <label class="label" for="confirmPassword">Confirm password <i class="req" aria-hidden="true">*</i></label>
          <input id="confirmPassword" class="input" [type]="showPassword() ? 'text' : 'password'" formControlName="confirmPassword" autocomplete="new-password"
                 [readonly]="submitting()" [attr.aria-invalid]="showMismatch() ? 'true' : null" aria-describedby="confirmPassword-error" />
          @if (showMismatch()) { <p id="confirmPassword-error" class="field-error">Passwords don't match.</p> }
        </div>

        <div class="field">
          <label class="row small">
            <input type="checkbox" formControlName="acceptTerms" [attr.aria-disabled]="submitting() ? 'true' : null" />
            <span>I agree to the <a href="#" (click)="$event.preventDefault()">Terms of Service</a> and <a href="#" (click)="$event.preventDefault()">Privacy Policy</a>. <i class="req" aria-hidden="true">*</i></span>
          </label>
        </div>

        <div>
          <button type="submit" class="btn btn-primary" [disabled]="!canSubmit()" [attr.aria-busy]="submitting()">
            @if (submitting()) { <span class="spinner" aria-hidden="true"></span> }
            Create account
          </button>
        </div>
      </form>
    </app-auth-layout>
  `,
  styles: `
    .title { margin-bottom: var(--space-4); }
    app-error-summary { display: block; margin-bottom: var(--space-4); }
  `,
})
export class SignupComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);
  private readonly summary = viewChild.required(ErrorSummaryComponent);
  private readonly injector = inject(Injector);

  readonly form = new FormGroup(
    {
      email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email, Validators.maxLength(320)] }),
      fullName: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(2), Validators.maxLength(120)] }),
      organization: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(2), Validators.maxLength(120)] }),
      password: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(12), Validators.maxLength(128)] }),
      confirmPassword: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
      acceptTerms: new FormControl(false, { nonNullable: true, validators: [Validators.requiredTrue] }),
    },
    { validators: [passwordsMatch] },
  );

  readonly submitting = signal(false);
  readonly showPassword = signal(false);
  readonly serverErrors = signal<SummaryEntry[]>([]);
  readonly summaryTitle = computed(() => `${this.serverErrors().length} problem${this.serverErrors().length === 1 ? ' needs' : 's need'} attention`);

  // Debounced so the live region does not chatter on every keystroke.
  private readonly passwordValue = toSignal(this.form.controls.password.valueChanges.pipe(debounceTime(500)), { initialValue: '' });
  readonly strength = computed(() => strengthOf(this.passwordValue()));

  private readonly formState = toSignal(this.form.valueChanges.pipe(map(() => this.form.getRawValue())), { initialValue: this.form.getRawValue() });
  readonly canSubmit = computed(() => {
    const v = this.formState();
    return !this.submitting() && !!v.email && !!v.fullName && !!v.organization && !!v.password && !!v.confirmPassword && v.acceptTerms;
  });

  showError(name: keyof typeof this.form.controls): boolean {
    const control = this.form.controls[name];
    return control.invalid && control.touched;
  }

  showMismatch(): boolean {
    return this.form.hasError('passwordMismatch') && this.form.controls.confirmPassword.touched;
  }

  errorFor(name: keyof typeof this.form.controls): string {
    const errors = this.form.controls[name].errors ?? {};
    if (typeof errors['server'] === 'string') return errors['server'];
    if (errors['required']) return `${FIELD_LABELS[name]} is required.`;
    if (errors['email']) return 'Enter a valid email address.';
    if (errors['minlength']) {
      return name === 'password' ? 'Password must be at least 12 characters.' : `${FIELD_LABELS[name]} must be at least ${errors['minlength'].requiredLength} characters.`;
    }
    if (errors['maxlength']) return `${FIELD_LABELS[name]} is too long.`;
    return 'Enter a valid value.';
  }

  async submit(): Promise<void> {
    this.form.markAllAsTouched();
    if (!this.canSubmit() || this.form.invalid) {
      return;
    }

    this.submitting.set(true);
    this.serverErrors.set([]);
    try {
      const { email, fullName, organization, password } = this.form.getRawValue();
      await this.auth.signup({ email, fullName, organization, password });
      this.toasts.info("Account created. We'll ask you to verify your email address soon.");
      await this.router.navigate(['/']);
    } catch (error) {
      this.handle(error);
    } finally {
      this.submitting.set(false);
    }
  }

  private handle(error: unknown): void {
    const problem = asProblem(error);
    if (error instanceof HttpErrorResponse && error.status === 400 && problem) {
      const entries = applyServerErrors(this.form, problem).map((e) => ({
        targetId: e.control,
        label: FIELD_LABELS[e.key] ?? e.key,
        message: e.message,
      }));
      this.serverErrors.set(entries);
      // Wireframe 1.1: on failed submit, focus moves to the first errored input.
      const first = entries.find((e) => e.targetId);
      focusAfterRender(this.injector, () => (first?.targetId ? document.getElementById(first.targetId) : null) ?? bannerOf(this.summary()));
    } else if (error instanceof HttpErrorResponse && error.status === 429) {
      this.serverErrors.set([{ targetId: null, label: 'Too many attempts', message: `Try again in ${problem?.retryAfterSeconds ?? 60} seconds.` }]);
      focusAfterRender(this.injector, () => bannerOf(this.summary()));
    } else {
      this.serverErrors.set([{ targetId: null, label: 'Connection problem', message: "We couldn't reach the server. Nothing you typed has been lost." }]);
      focusAfterRender(this.injector, () => bannerOf(this.summary()));
    }
  }
}
