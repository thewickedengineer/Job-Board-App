import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, afterNextRender, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { closesSoon, closesText, daysUntil, formatDate, lines, postedAgo, salaryCompact, salaryText } from '../../core/format';
import { JobsService } from '../../core/jobs.service';
import { JobDetail, label } from '../../core/models';

/** A freshly published posting takes a couple of seconds to be projected; poll before declaring a 404. */
const PUBLISH_GRACE_ATTEMPTS = 4;
const PUBLISH_GRACE_DELAY_MS = 2000;

/**
 * Wireframe 2.3 / 2.4-B. States: loading skeleton · open · closing soon ·
 * closed/expired (banner + disabled Apply) · "being published" (a slug not yet
 * projected) · 404 for a slug that never existed · error.
 */
@Component({
  selector: 'app-detail',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './detail.component.html',
  styleUrl: './detail.component.scss',
})
export class DetailComponent {
  readonly slug = input.required<string>();

  private readonly jobs = inject(JobsService);
  private readonly title = inject(Title);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly job = signal<JobDetail | null>(null);
  readonly state = signal<'loading' | 'ready' | 'publishing' | 'notFound' | 'error'>('loading');
  readonly copied = signal(false);
  readonly headerOffscreen = signal(false);

  private readonly header = viewChild<ElementRef<HTMLElement>>('header');
  private observer: IntersectionObserver | null = null;
  private graceTimer: ReturnType<typeof setTimeout> | null = null;

  readonly label = label;
  /** Router state from a results card: lets the back link restore the filtered list via history. */
  private readonly origin = (inject(Router).getCurrentNavigation()?.extras.state ?? null) as { fromResults?: boolean; total?: number } | null;
  readonly fromResults = !!this.origin?.fromResults;
  readonly backText = this.origin?.total !== undefined && this.origin.total !== null ? `Back to ${this.origin.total} results` : 'Back to all roles';

  goBack(event: Event): void {
    if (this.fromResults && history.length > 1) {
      event.preventDefault();
      history.back();
    }
  }
  readonly salary = computed(() => (this.job() ? salaryText(this.job()!) : ''));
  readonly posted = computed(() => (this.job() ? postedAgo(this.job()!.publishedAt) : ''));
  readonly closes = computed(() => (this.job() ? closesText(this.job()!.closingDate) : ''));
  readonly closingSoon = computed(() => !!this.job() && this.job()!.isOpen && closesSoon(this.job()!.closingDate));
  readonly closingDateText = computed(() => (this.job() ? formatDate(this.job()!.closingDate) : ''));
  /** Expired (deadline passed) vs. closed early by the manager — the API only exposes the deadline. */
  readonly expired = computed(() => !!this.job() && daysUntil(this.job()!.closingDate) < 0);
  readonly responsibilities = computed(() => lines(this.job()?.responsibilities ?? null));
  readonly requirements = computed(() => lines(this.job()?.requirements ?? null));
  readonly applyHref = computed(() => {
    const j = this.job();
    if (!j) return null;
    return j.applicationUrl ?? (j.applicationEmail ? `mailto:${j.applicationEmail}?subject=${encodeURIComponent(`Application: ${j.title}`)}` : null);
  });
  readonly applyExternal = computed(() => !!this.job()?.applicationUrl);
  readonly similar = computed(() => (this.job()?.similar ?? []).map((s) => ({ ...s, salary: salaryCompact(s) })));

  constructor() {
    effect(() => {
      const slug = this.slug();
      untracked(() => void this.load(slug, 0));
    });

    // Sticky apply: once the header scrolls out of view the floating bar appears.
    effect(() => {
      const header = this.header()?.nativeElement;
      this.observer?.disconnect();
      if (!header || typeof IntersectionObserver === 'undefined') return;
      this.observer = new IntersectionObserver(([entry]) => this.headerOffscreen.set(!entry.isIntersecting), { rootMargin: '-56px 0px 0px 0px' });
      this.observer.observe(header);
    });

    this.destroyRef.onDestroy(() => {
      this.observer?.disconnect();
      if (this.graceTimer) clearTimeout(this.graceTimer);
    });
  }

  private async load(slug: string, attempt: number): Promise<void> {
    if (attempt === 0) this.state.set('loading');
    try {
      const job = await firstValueFrom(this.jobs.detail(slug));
      this.job.set(job);
      this.state.set('ready');
      this.title.setTitle(`${job.title} · ${job.organization} · TalentBridge`);
      afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>('h1')?.focus(), { injector: this.injector });
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 404) {
        // Deep link to a slug that may simply not have propagated yet.
        if (attempt < PUBLISH_GRACE_ATTEMPTS) {
          this.state.set('publishing');
          this.graceTimer = setTimeout(() => void this.load(slug, attempt + 1), PUBLISH_GRACE_DELAY_MS);
        } else {
          this.state.set('notFound');
          this.title.setTitle('Listing not found · TalentBridge');
        }
      } else {
        this.state.set('error');
      }
    }
  }

  retry(): void {
    void this.load(this.slug(), 0);
  }

  async copyLink(): Promise<void> {
    try {
      await navigator.clipboard.writeText(location.href);
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 3000);
    } catch {
      this.copied.set(false);
    }
  }

  mailShareHref(): string {
    const j = this.job();
    return j ? `mailto:?subject=${encodeURIComponent(`${j.title} at ${j.organization}`)}&body=${encodeURIComponent(location.href)}` : '#';
  }

  linkedInShareHref(): string {
    return `https://www.linkedin.com/sharing/share-offsite/?url=${encodeURIComponent(location.href)}`;
  }
}
