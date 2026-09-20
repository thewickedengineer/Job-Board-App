import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, ElementRef, afterNextRender, computed, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { JobPostingsService } from '../../core/job-postings.service';
import { JobPostingResponse } from '../../core/models';
import { ToastService } from '../../core/toast.service';
import { StatusChipComponent } from '../../shared/status-chip.component';

/**
 * Wireframe 1.5: echoes the API record verbatim. The record arrives via router
 * state from the create screen; on a hard refresh it is re-fetched by id, which
 * is still the server's copy, never a client one.
 */
@Component({
  selector: 'app-confirmation',
  imports: [RouterLink, DatePipe, StatusChipComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (record(); as r) {
      <p class="visually-hidden" aria-live="polite">{{ r.status === 'Draft' ? 'Draft saved' : 'Posting published' }}</p>
      <header class="head">
        <h1 #heading tabindex="-1">{{ r.status === 'Draft' ? 'Draft saved' : 'Posting published' }}</h1>
        <p class="muted">{{ r.title }} · {{ r.department }} · {{ r.location }}</p>
        @if (r.status === 'Draft') {
          <p>This draft is saved and is not visible to candidates. You can keep editing it whenever you like.</p>
        } @else {
          <p>This posting is saved. It may take a few moments to appear on the public job board while the listing propagates — you don't need to do anything.</p>
        }
      </header>

      <section class="card" aria-labelledby="record-title">
        <h2 id="record-title">Saved record (as returned by the API)</h2>
        <dl class="record">
          <dt>ID</dt><dd><button type="button" class="copy mono" (click)="copy(r.id, 'ID')" title="Copy ID">{{ r.id }}</button></dd>
          <dt>Slug</dt><dd><button type="button" class="copy mono" (click)="copy(r.slug, 'Slug')" title="Copy slug">{{ r.slug }}</button></dd>
          <dt>Created</dt><dd>{{ r.createdAt | date: 'dd MMM yyyy, HH:mm zzzz' }}</dd>
          <dt>Status</dt><dd><app-status-chip [status]="r.status" /></dd>
          <dt>Closing date</dt><dd>{{ r.closingDate | date: 'dd MMM yyyy' }}</dd>
          <dt>Internal reference</dt><dd>{{ r.referenceCode ?? '—' }}</dd>
          <dt>Version</dt><dd>{{ r.version }}</dd>
        </dl>
      </section>

      <div class="row actions">
        @if (r.status === 'Draft') {
          <a [routerLink]="['/postings', r.id]" class="btn btn-primary">Continue editing</a>
        } @else {
          <a [href]="boardUrl()" target="_blank" rel="noopener" class="btn btn-primary">View on job board ↗</a>
        }
        <a routerLink="/postings/new" class="btn">Post another</a>
        <a routerLink="/" class="btn btn-ghost">Back to dashboard</a>
      </div>
    } @else if (missing()) {
      <section class="card">
        <h1>We can't find that posting</h1>
        <p><a routerLink="/" class="btn">Back to dashboard</a></p>
      </section>
    } @else {
      <div class="stack" aria-busy="true"><span class="skeleton" style="width: 40%; height: 24px"></span><span class="skeleton" style="width: 70%"></span></div>
    }
  `,
  styles: `
    .head { display: flex; flex-direction: column; gap: var(--space-2); margin-bottom: var(--space-4); }
    h1 { outline: none; }
    .record { display: grid; grid-template-columns: max-content 1fr; gap: var(--space-2) var(--space-4); margin: var(--space-3) 0 0; }
    .record dt { font-size: var(--text-xs); font-weight: 650; color: #4a4a4a; text-transform: uppercase; letter-spacing: 0.05em; padding-top: 3px; }
    .record dd { margin: 0; }
    .copy { border: 0; background: transparent; padding: 0; cursor: copy; color: var(--color-ink); text-decoration: underline dotted; }
    .actions { margin-top: var(--space-4); }
  `,
})
export class ConfirmationComponent {
  readonly id = input.required<string>();

  private readonly api = inject(JobPostingsService);
  private readonly toasts = inject(ToastService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly record = signal<JobPostingResponse | null>(
    (inject(Router).getCurrentNavigation()?.extras.state?.['record'] as JobPostingResponse | undefined) ?? null,
  );
  readonly missing = signal(false);
  readonly boardUrl = computed(() => `${environment.jobBoardUrl}/jobs/${this.record()?.slug ?? ''}`);

  constructor() {
    afterNextRender(async () => {
      if (!this.record()) {
        try {
          this.record.set(await firstValueFrom(this.api.get(this.id())));
        } catch {
          this.missing.set(true);
        }
      }
      // Focus moves to the h1 on arrival.
      queueMicrotask(() => this.host.nativeElement.querySelector<HTMLElement>('h1')?.focus());
    });
  }

  async copy(value: string, what: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(value);
      this.toasts.info(`${what} copied.`);
    } catch {
      this.toasts.error(`Couldn't copy the ${what.toLowerCase()}.`);
    }
  }
}
