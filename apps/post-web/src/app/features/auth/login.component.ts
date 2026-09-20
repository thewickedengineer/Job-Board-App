import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, computed, inject, signal, viewChild } from '@angular/core';
import { focusAfterRender } from '../../core/focus';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { asProblem } from '../../core/problem-details';
import { AuthLayoutComponent } from './auth-layout.component';

/**
 * Wireframe 1.2: default · submitting · 401 banner (generic) · 429 lockout with
 * a client-side countdown that re-enables the form at zero · success → dashboard
 * (or the URL the manager was trying to reach).
 */
@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, AuthLayoutComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-auth-layout asideText="No account?" asideLink="/signup" asideLabel="Sign up">
      <h1 class="title">Log in to Post</h1>

      @if (failure(); as message) {
        <div #alert class="banner banner-error" role="alert" tabindex="-1">{{ message }}</div>
      }
      @if (lockedSeconds() > 0) {
        <div #alert class="banner banner-warn" role="alert" tabindex="-1">
          Too many attempts. Try again in <span aria-live="off">{{ countdown() }}</span>.
        </div>
      }

      <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="stack" [attr.aria-busy]="submitting()">
        <div class="field">
          <label class="label" for="email">Email</label>
          <input id="email" class="input" type="email" formControlName="email" autocomplete="email" placeholder="you@company.com"
                 [readonly]="locked()" [class.is-locked]="locked()" [attr.aria-disabled]="locked() ? 'true' : null"
                 [attr.aria-invalid]="showError('email') ? 'true' : null" aria-describedby="email-error" />
          @if (showError('email')) { <p id="email-error" class="field-error">Email is required.</p> }
        </div>

        <div class="field">
          <label class="label" for="password">Password</label>
          <div class="input-group">
            <input id="password" class="input" [type]="showPassword() ? 'text' : 'password'" formControlName="password" autocomplete="current-password"
                   [readonly]="locked()" [class.is-locked]="locked()" [attr.aria-disabled]="locked() ? 'true' : null"
                   [attr.aria-invalid]="showError('password') ? 'true' : null" aria-describedby="password-error" />
            <button type="button" class="btn btn-ghost btn-sm input-addon" (click)="showPassword.set(!showPassword())" [attr.aria-pressed]="showPassword()">
              {{ showPassword() ? 'Hide' : 'Show' }}
            </button>
          </div>
          @if (showError('password')) { <p id="password-error" class="field-error">Password is required.</p> }
        </div>

        <div class="row-between">
          <button type="submit" class="btn btn-primary" [disabled]="submitting() || locked()" [attr.aria-busy]="submitting()">
            @if (submitting()) { <span class="spinner" aria-hidden="true"></span> }
            Log in
          </button>
        </div>
      </form>
    </app-auth-layout>
  `,
  styles: `
    .title { margin-bottom: var(--space-4); }
    .banner { margin-bottom: var(--space-4); }
  `,
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly alert = viewChild<ElementRef<HTMLElement>>('alert');

  readonly form = new FormGroup({
    email: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  readonly submitting = signal(false);
  readonly failure = signal<string | null>(null);
  readonly showPassword = signal(false);
  readonly lockedSeconds = signal(0);
  readonly locked = computed(() => this.lockedSeconds() > 0);
  readonly countdown = computed(() => {
    const s = this.lockedSeconds();
    return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
  });

  private timer: ReturnType<typeof setInterval> | null = null;

  constructor() {
    this.destroyRef.onDestroy(() => this.stopTimer());
  }

  showError(name: 'email' | 'password'): boolean {
    const control = this.form.controls[name];
    return control.invalid && (control.touched || this.form.touched);
  }

  async submit(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.submitting() || this.locked()) {
      return;
    }

    this.submitting.set(true);
    this.failure.set(null);
    try {
      const { email, password } = this.form.getRawValue();
      await this.auth.login(email, password);
      const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
      await this.router.navigateByUrl(returnUrl && returnUrl.startsWith('/') ? returnUrl : '/');
    } catch (error) {
      this.handle(error);
    } finally {
      this.submitting.set(false);
    }
  }

  private handle(error: unknown): void {
    const problem = asProblem(error);
    if (error instanceof HttpErrorResponse && error.status === 429) {
      this.startLockout(problem?.retryAfterSeconds ?? 60);
    } else if (error instanceof HttpErrorResponse && error.status === 401) {
      this.failure.set(problem?.detail ?? "We couldn't match that email and password.");
    } else {
      this.failure.set("We couldn't reach the server. Check your connection and try again.");
    }
    focusAfterRender(this.injector, () => this.alert()?.nativeElement);
  }

  private startLockout(seconds: number): void {
    this.stopTimer();
    this.lockedSeconds.set(seconds);
    this.timer = setInterval(() => {
      const next = this.lockedSeconds() - 1;
      this.lockedSeconds.set(Math.max(0, next));
      if (next <= 0) {
        this.stopTimer();
      }
    }, 1000);
  }

  private stopTimer(): void {
    if (this.timer) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }
}
