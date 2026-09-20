import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { DialogService } from '../../core/dialog.service';
import { JobPostingsService } from '../../core/job-postings.service';
import { asProblem } from '../../core/problem-details';
import { ToastService } from '../../core/toast.service';
import { FormSubmission, JobPostingFormComponent } from './job-posting-form.component';

/** Wireframe 1.4: the create flow. Success hands the server's record to 1.5. */
@Component({
  selector: 'app-create-posting',
  imports: [RouterLink, JobPostingFormComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav class="breadcrumb small muted" aria-label="Breadcrumb">
      <a routerLink="/">Postings</a> / New
    </nav>
    <h1 class="page-title">New job posting</h1>
    <app-job-posting-form #form mode="create" [submitting]="submitting()" (submitted)="save($event)" (cancelled)="cancel()" />
  `,
  styles: `
    .breadcrumb { margin-bottom: var(--space-2); }
    .page-title { margin-bottom: var(--space-4); }
  `,
})
export class CreatePostingComponent {
  private readonly api = inject(JobPostingsService);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);
  private readonly dialogs = inject(DialogService);
  private readonly form = viewChild.required(JobPostingFormComponent);

  readonly submitting = signal(false);
  private saved = false;

  async save({ request }: FormSubmission): Promise<void> {
    this.submitting.set(true);
    try {
      const record = await firstValueFrom(this.api.create(request));
      this.saved = true;
      // The confirmation screen renders exactly what came back — never a client copy.
      await this.router.navigate(['/postings', record.id, 'confirmation'], { state: { record } });
    } catch (error) {
      this.submitting.set(false);
      const problem = asProblem(error);
      if (error instanceof HttpErrorResponse && error.status === 400 && problem) {
        this.form().applyProblem(problem);
      } else if (error instanceof HttpErrorResponse && error.status === 0) {
        this.form().fail('Connection problem', "We couldn't reach the server. Nothing you typed has been lost — try again.");
      } else {
        this.form().fail('Something went wrong', problem?.detail ?? problem?.title ?? 'The posting was not saved. Please try again.');
        this.toasts.error("Couldn't save the posting.");
      }
    }
  }

  async cancel(): Promise<void> {
    if (await this.canLeave()) {
      await this.router.navigate(['/']);
    }
  }

  /** Route guard: block while submitting, confirm when dirty. */
  async canLeave(): Promise<boolean> {
    if (this.saved) return true; // the navigation to the confirmation screen
    if (this.submitting()) return false;
    if (!this.form().dirty()) return true;
    return this.dialogs.confirm({
      title: 'Discard this posting?',
      body: "You have unsaved changes. If you leave now they'll be lost.",
      confirmLabel: 'Discard',
      cancelLabel: 'Keep editing',
      destructive: true,
    });
  }
}
