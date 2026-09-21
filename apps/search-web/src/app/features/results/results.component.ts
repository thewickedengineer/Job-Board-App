import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, Injector, afterNextRender, computed, effect, inject, linkedSignal, signal, untracked, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { JobsService } from '../../core/jobs.service';
import { Facets, JobFilters, JobSummary, PAGE_SIZE, PagedResponse, SORTS, SORT_LABELS, label } from '../../core/models';
import { FilterRailComponent, countActive } from './filter-rail.component';
import { JobCardComponent } from './job-card.component';

interface Chip {
  key: string;
  text: string;
  remove: Partial<JobFilters>;
}

interface Suggestion {
  /** The quoted filter value(s) this relaxation removes. */
  values: string;
  count: number;
  apply: Partial<JobFilters>;
}

const AUTO_RETRIES = 2;
const RETRY_DELAYS_MS = [1000, 3000];

/**
 * Wireframe 2.1 / 2.2 / 2.4-A. Every filter lives in the URL; the list and
 * the facets are httpResources keyed on it. "Load more" appends pages.
 * States: loading (skeleton after 200 ms), populated, no results (with
 * suggestions from facet counts), error/offline (after two silent retries).
 */
@Component({
  selector: 'app-results',
  imports: [FilterRailComponent, JobCardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './results.component.html',
  styleUrl: './results.component.scss',
})
export class ResultsComponent {
  private readonly jobs = inject(JobsService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly injector = inject(Injector);

  readonly sorts = SORTS;
  readonly sortLabels = SORT_LABELS;

  // --- URL → filters ----------------------------------------------------------------
  private readonly params = toSignal(this.route.queryParamMap, { initialValue: this.route.snapshot.queryParamMap });
  readonly filters = computed(() => this.jobs.fromQueryParams((k) => this.params().get(k), (k) => this.params().getAll(k)));
  readonly filterKey = computed(() => this.jobs.facetParams(this.filters()).toString() + '|' + this.filters().sort);
  readonly activeCount = computed(() => countActive(this.filters()));

  // --- data -----------------------------------------------------------------------------
  readonly page = httpResource<PagedResponse<JobSummary>>(() => ({ url: `${this.jobs.base}/jobs`, params: this.jobs.listParams(this.filters()) }));
  readonly facets = httpResource<Facets>(() => ({ url: `${this.jobs.base}/facets`, params: this.jobs.facetParams(this.filters()) }));

  // resource.value() throws while a resource is in its error state, so every
  // read goes through these guards.
  readonly pageValue = computed(() => (this.page.hasValue() ? this.page.value() : undefined));
  readonly facetsValue = computed(() => (this.facets.hasValue() ? this.facets.value() : undefined));

  /** Accumulates pages while the filters stay the same; replaces when they change. */
  readonly items = linkedSignal<{ value: PagedResponse<JobSummary> | undefined; key: string }, JobSummary[]>({
    source: () => ({ value: this.pageValue(), key: this.filterKey() }),
    computation: (src, prev) => {
      if (!src.value) return prev?.source.key === src.key ? prev.value : [];
      const append = prev !== undefined && prev.source.key === src.key && src.value.page > 1;
      return append ? [...prev.value, ...src.value.items] : src.value.items;
    },
  });

  readonly total = computed(() => this.pageValue()?.total ?? 0);
  readonly remaining = computed(() => Math.max(0, this.total() - this.items().length));
  readonly hasMore = computed(() => this.remaining() > 0 && (this.pageValue()?.page ?? 0) < (this.pageValue()?.totalPages ?? 0));
  readonly loadingMore = computed(() => this.page.isLoading() && this.items().length > 0);
  readonly showSkeleton = signal(false);
  readonly skeletonCards = [1, 2, 3];
  readonly listBusy = computed(() => this.page.isLoading());

  // --- failure handling: two silent retries with backoff, then the panel -----------
  readonly failed = signal(false);
  readonly offline = signal(false);
  private retries = 0;
  private readonly retryPanel = viewChild<ElementRef<HTMLElement>>('retryPanel');

  // --- chips & empty-state suggestions -------------------------------------------------
  readonly chips = computed<Chip[]>(() => {
    const f = this.filters();
    const chips: Chip[] = [];
    for (const d of f.department) chips.push({ key: `d:${d}`, text: d, remove: { department: f.department.filter((x) => x !== d), page: 1 } });
    if (f.location.trim()) chips.push({ key: 'loc', text: f.location.trim(), remove: { location: '', page: 1 } });
    for (const w of f.workArrangement) chips.push({ key: `w:${w}`, text: label(w), remove: { workArrangement: f.workArrangement.filter((x) => x !== w), page: 1 } });
    for (const e of f.employmentType) chips.push({ key: `e:${e}`, text: label(e), remove: { employmentType: f.employmentType.filter((x) => x !== e), page: 1 } });
    for (const s of f.seniority) chips.push({ key: `s:${s}`, text: s, remove: { seniority: f.seniority.filter((x) => x !== s), page: 1 } });
    if (f.salaryMin !== null || f.salaryMax !== null) {
      const k = (n: number) => `${Math.round(n / 1000)}k`;
      const text = f.salaryMin !== null && f.salaryMax !== null ? `${k(f.salaryMin)}–${k(f.salaryMax)}` : f.salaryMin !== null ? `${k(f.salaryMin)}+` : `up to ${k(f.salaryMax!)}`;
      chips.push({ key: 'sal', text, remove: { salaryMin: null, salaryMax: null, page: 1 } });
    }
    if (f.postedWithinDays !== null) chips.push({ key: 'pw', text: f.postedWithinDays === 1 ? 'Last 24 hours' : `Last ${f.postedWithinDays} days`, remove: { postedWithinDays: null, page: 1 } });
    return chips;
  });

  /**
   * Facet counts are computed with each dimension's own filter excluded, so the
   * sum of a dimension's counts is exactly what removing that filter returns.
   * Only relaxations known to return results are offered (wireframe 2.2-B).
   */
  readonly suggestions = computed<Suggestion[]>(() => {
    const f = this.filters();
    const facets = this.facetsValue();
    if (!facets) return [];
    const sum = (rows: { count: number }[]) => rows.reduce((n, r) => n + r.count, 0);
    const out: Suggestion[] = [];
    const quoted = (values: string[]) => values.map((v) => `“${v}”`).join(', ');
    if (f.department.length) out.push({ values: quoted(f.department), count: sum(facets.departments), apply: { department: [], page: 1 } });
    if (f.workArrangement.length) out.push({ values: quoted(f.workArrangement.map(label)), count: sum(facets.workArrangements), apply: { workArrangement: [], page: 1 } });
    if (f.employmentType.length) out.push({ values: quoted(f.employmentType.map(label)), count: sum(facets.employmentTypes), apply: { employmentType: [], page: 1 } });
    if (f.seniority.length) out.push({ values: quoted(f.seniority), count: sum(facets.seniorities), apply: { seniority: [], page: 1 } });
    return out.filter((s) => s.count > 0).sort((a, b) => b.count - a.count);
  });

  readonly announcement = computed(() => {
    if (this.failed()) return "Couldn't load jobs";
    if (this.page.isLoading() && this.items().length === 0) return 'Loading jobs';
    const n = this.total();
    return n === 0 ? 'No jobs found' : `${n} job${n === 1 ? '' : 's'} found`;
  });

  // --- mobile filter sheet -------------------------------------------------------------
  readonly sheetOpen = signal(false);
  private readonly sheet = viewChild<ElementRef<HTMLElement>>('sheet');
  private sheetOpener: HTMLElement | null = null;

  constructor() {
    // Skeleton only after 200 ms, and only for a fresh list (load-more shows a button spinner).
    effect((onCleanup) => {
      const fresh = this.page.isLoading() && this.items().length === 0;
      if (!fresh) {
        untracked(() => this.showSkeleton.set(false));
        return;
      }
      const t = setTimeout(() => this.showSkeleton.set(true), 200);
      onCleanup(() => clearTimeout(t));
    });

    // Auto-retry twice with backoff before surfacing the error panel.
    effect((onCleanup) => {
      const status = this.page.status();
      if (status === 'resolved' || status === 'local') {
        // Only a successful response resets the budget; a reload in flight does not.
        this.retries = 0;
        untracked(() => this.failed.set(false));
        return;
      }
      if (status !== 'error') return;
      if (this.retries < AUTO_RETRIES) {
        const delay = RETRY_DELAYS_MS[this.retries];
        const t = setTimeout(() => {
          this.retries++;
          this.page.reload();
        }, delay);
        onCleanup(() => clearTimeout(t));
        return;
      }
      const t = setTimeout(() => {
        this.offline.set(typeof navigator !== 'undefined' && !navigator.onLine);
        this.failed.set(true);
        afterNextRender(() => this.retryPanel()?.nativeElement.querySelector<HTMLElement>('button')?.focus(), { injector: this.injector });
      }, 0);
      onCleanup(() => clearTimeout(t));
    });

  }

  setSort(sort: string): void {
    this.patch({ sort: SORTS.includes(sort as (typeof SORTS)[number]) ? (sort as (typeof SORTS)[number]) : 'recent', page: 1 });
  }

  apply(change: Partial<JobFilters>): void {
    this.patch(change);
  }

  clearAll(): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: {} });
  }

  loadMore(): void {
    const firstNew = this.items().length;
    this.patch({ page: this.filters().page + 1 }, /* replaceUrl */ true);
    // Focus the first newly appended card once it renders (wireframe 2.1).
    const stop = effect(() => {
      if (this.items().length > firstNew) {
        afterNextRender(() => {
          const cards = document.querySelectorAll<HTMLElement>('.job-card a');
          cards[firstNew]?.focus();
        }, { injector: this.injector });
        stop.destroy();
      }
    }, { injector: this.injector });
  }

  retry(): void {
    this.retries = 0;
    this.failed.set(false);
    this.page.reload();
  }

  openSheet(opener: HTMLElement): void {
    this.sheetOpener = opener;
    this.sheetOpen.set(true);
    afterNextRender(() => this.sheet()?.nativeElement.querySelector<HTMLElement>('h2')?.focus(), { injector: this.injector });
  }

  closeSheet(): void {
    this.sheetOpen.set(false);
    this.sheetOpener?.focus();
    this.sheetOpener = null;
  }

  onSheetKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      this.closeSheet();
    } else if (event.key === 'Tab') {
      const panel = this.sheet()?.nativeElement;
      if (!panel) return;
      const focusable = Array.from(panel.querySelectorAll<HTMLElement>('input, select, button:not(:disabled), [tabindex="0"]'));
      if (focusable.length === 0) return;
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    }
  }

  readonly pageSize = PAGE_SIZE;

  private patch(change: Partial<JobFilters>, replaceUrl = false): void {
    const next = { ...this.filters(), ...change };
    void this.router.navigate([], { relativeTo: this.route, queryParams: this.jobs.toQueryParams(next), replaceUrl });
  }
}
