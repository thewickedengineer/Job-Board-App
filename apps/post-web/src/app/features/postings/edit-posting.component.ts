import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../core/auth.service';
import { DialogService } from '../../core/dialog.service';
import { JobPostingsService } from '../../core/job-postings.service';
import { JobPostingResponse } from '../../core/models';
import { asProblem } from '../../core/problem-details';
import { ToastService } from '../../core/toast.service';
import { StatusChipComponent } from '../../shared/status-chip.component';
import { FormSubmission, JobPostingFormComponent } from './job-posting-form.component';

/**
 * Wireframe 1.6: hydrate from GET, save with the loaded version (409 → conflict
 * banner offering reload or overwrite), close with a typed confirmation.
 * States: loading skeleton · not found · populated · conflict · saving · closed.
 */
@Component({
  selector: 'app-edit-posting',
  imports: [RouterLink, DatePipe, StatusChipComponent, JobPostingFormComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav class="breadcrumb small muted" aria-label="Breadcrumb"><a routerLink="/">Postings</a> / Edit</nav>

    @if (loading()) {
      <div class="stack" aria-busy="true">
        <span class="skeleton" style="width: 40%; height: 24px"></span>
        <span class="skeleton" style="width: 60%"></span>
        @for (i of [1, 2, 3]; track i) { <div class="card"><span class="skeleton" style="width: 30%"></span><br /><span class="skeleton"></span></div> }
      </div>
    } @else if (notFound()) {
      <section class="card empty" aria-labelledby="nf-title">
        <h1 id="nf-title">We can't find that posting</h1>
        <p class="muted">It may have been removed, or it belongs to another account.</p>
        <p><a routerLink="/" class="btn">Back to postings</a></p>
      </section>
    } @else if (record(); as r) {
      <header class="edit-head">
        <div class="row-between">
          <div>
            <h1>{{ r.title }}</h1>
            <p class="small muted">
              <span class="mono">{{ r.id }}</span> · <app-status-chip [status]="r.status" /> · Last updated {{ r.updatedAt | date: 'dd MMM yyyy, HH:mm' }} by {{ managerName() }}
            </p>
          </div>
          <div class="row">
            <a [href]="boardUrl()" target="_blank" rel="noopener" class="btn">View on board ↗</a>
            @if (r.status === 'Published') {
              <button type="button" class="btn btn-danger" (click)="closePosting()" [disabled]="submitting()">Close posting</button>
            }
          </div>
        </div>
        @if (r.status === 'Closed') {
          <div class="banner banner-info" role="status">This posting is closed and can't be edited or re-opened. Post a new one to hire for this role again.</div>
        } @else if (r.status === 'Expired') {
          <div class="banner banner-warn" role="status">This posting's closing date has passed, so it is no longer on the public board. Extend the closing date and save to republish it.</div>
        }
        @if (conflict(); as c) {
          <div class="banner banner-warn" role="alert" tabindex="-1" #conflictBanner>
            <p class="banner-title">This posting changed since you loaded it.</p>
            <p class="small">{{ c }}</p>
            <p class="row">
              <button type="button" class="btn btn-sm" (click)="reload()">Reload latest (discard my edits)</button>
              <button type="button" class="btn btn-sm btn-danger" (click)="overwrite()">Overwrite with my edits</button>
            </p>
          </div>
        }
      </header>
      <app-job-posting-form #form mode="edit" [initial]="r" [submitting]="submitting()" (submitted)="save($event)" (cancelled)="discard()" />
    }
  `,
  styles: `
    .breadcrumb { margin-bottom: var(--space-2); }
    .edit-head { display: flex; flex-direction: column; gap: var(--space-3); margin-bottom: var(--space-4); }
    .empty { text-align: center; padding: var(--space-7) var(--space-4); display: flex; flex-direction: column; gap: var(--space-3); align-items: center; }
  `,
})
export class EditPostingComponent {
  readonly id = input.required<string>();

  private readonly api = inject(JobPostingsService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);
  private readonly dialogs = inject(DialogService);
  private readonly form = viewChild(JobPostingFormComponent);

  readonly record = signal<JobPostingResponse | null>(null);
  readonly loading = signal(true);
  readonly notFound = signal(false);
  readonly submitting = signal(false);
  readonly conflict = signal<string | null>(null);
  private pendingSubmission: FormSubmission | null = null;

  readonly managerName = computed(() => this.auth.manager()?.fullName ?? 'you');
  readonly boardUrl = computed(() => `${environment.jobBoardUrl}/jobs/${this.record()?.slug ?? ''}`);

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => void this.load(id));
    });
  }

  private async load(id: string): Promise<void> {
    this.loading.set(true);
    this.notFound.set(false);
    try {
      this.record.set(await firstValueFrom(this.api.get(id)));
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 404) {
        this.notFound.set(true);
      } else {
        this.toasts.error("Couldn't load the posting.", { label: 'Retry', run: () => void this.load(id) });
      }
    } finally {
      this.loading.set(false);
    }
  }

  async save(submission: FormSubmission, version = this.record()?.version): Promise<void> {
    const current = this.record();
    if (!current || version === undefined) return;

    this.submitting.set(true);
    this.conflict.set(null);
    try {
      const saved = await firstValueFrom(this.api.update(current.id, { ...submission.request, version }));
      this.record.set(saved);
      this.form()?.markPristine();
      this.toasts.success(submission.intent === 'publish' ? 'Posting published. It will appear on the board shortly.' : 'Changes saved.');
    } catch (error) {
      const problem = asProblem(error);
      if (error instanceof HttpErrorResponse && error.status === 409) {
        this.pendingSubmission = submission;
        this.conflict.set(problem?.detail ?? 'Another change was saved first.');
      } else if (error instanceof HttpErrorResponse && error.status === 400 && problem) {
        this.form()?.applyProblem(problem);
      } else if (error instanceof HttpErrorResponse && error.status === 0) {
        this.form()?.fail('Connection problem', "We couldn't reach the server. Your edits are still here — try again.");
      } else {
        this.form()?.fail('Something went wrong', problem?.detail ?? 'Your changes were not saved. Please try again.');
      }
    } finally {
      this.submitting.set(false);
    }
  }

  /** Conflict → take the server's copy, dropping local edits. */
  async reload(): Promise<void> {
    this.conflict.set(null);
    this.pendingSubmission = null;
    await this.load(this.id());
  }

  /** Conflict → resubmit my edits against the server's current version. */
  async overwrite(): Promise<void> {
    const submission = this.pendingSubmission;
    if (!submission) return;
    try {
      const latest = await firstValueFrom(this.api.get(this.id()));
      this.conflict.set(null);
      await this.save(submission, latest.version);
    } catch {
      this.toasts.error("Couldn't fetch the latest version.");
    }
  }

  async discard(): Promise<void> {
    const current = this.record();
    if (!current) return;
    if (await this.dialogs.confirm({ title: 'Discard changes?', body: 'Your edits will be lost and the saved posting will be shown.', confirmLabel: 'Discard', cancelLabel: 'Keep editing', destructive: true })) {
      // Re-hydrating from the same record resets the form.
      this.record.set({ ...current });
    }
  }

  async closePosting(): Promise<void> {
    const current = this.record();
    if (!current) return;
    const confirmed = await this.dialogs.confirm({
      title: 'Close this posting?',
      body: "It will be removed from the public job board within a few minutes. You can duplicate the posting later — but it can't be re-opened.",
      confirmLabel: 'Close posting',
      cancelLabel: 'Keep open',
      destructive: true,
      typeToConfirm: 'CLOSE',
    });
    if (!confirmed) return;

    this.submitting.set(true);
    try {
      this.record.set(await firstValueFrom(this.api.close(current.id)));
      this.toasts.success('Posting closed.');
    } catch {
      this.toasts.error("Couldn't close the posting.", { label: 'Retry', run: () => void this.closePosting() });
    } finally {
      this.submitting.set(false);
    }
  }

  async canLeave(): Promise<boolean> {
    if (this.submitting()) return false;
    if (!this.form()?.dirty()) return true;
    return this.dialogs.confirm({ title: 'Leave without saving?', body: 'You have unsaved changes.', confirmLabel: 'Leave', cancelLabel: 'Stay', destructive: true });
  }
}
