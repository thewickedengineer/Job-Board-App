import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { debounceTime, distinctUntilChanged, Subject } from 'rxjs';
import { DashboardQuery, JobPostingsService } from '../../core/job-postings.service';
import {
  DASHBOARD_SORTS,
  DASHBOARD_STATUSES,
  DashboardSort,
  DashboardStatus,
  JobPostingSummary,
  LABELS,
  PagedResponse,
} from '../../core/models';
import { StatusChipComponent } from '../../shared/status-chip.component';

const SORT_LABELS: Record<DashboardSort, string> = {
  createdDesc: 'Recently created',
  createdAsc: 'Oldest first',
  closingAsc: 'Closing soonest',
  closingDesc: 'Closing latest',
  titleAsc: 'Title A–Z',
  titleDesc: 'Title Z–A',
};

const PAGE_SIZE = 20;
const CLOSING_SOON_DAYS = 7;

/**
 * Wireframe 1.3 / 1.3b. Search, status and sort live in the URL so a view is
 * shareable and survives refresh; the list is an httpResource keyed on them.
 * States: loading (skeleton after 200 ms), populated, empty (new manager),
 * filtered-empty, error with retry.
 */
@Component({
  selector: 'app-dashboard',
  imports: [RouterLink, DatePipe, StatusChipComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent {
  private readonly api = inject(JobPostingsService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly sorts = DASHBOARD_SORTS;
  readonly statuses = DASHBOARD_STATUSES;
  readonly sortLabels = SORT_LABELS;
  readonly arrangementLabels = LABELS.workArrangement;

  // --- URL → query --------------------------------------------------------------
  private readonly params = toSignal(this.route.queryParamMap, { initialValue: this.route.snapshot.queryParamMap });

  readonly query = computed<DashboardQuery>(() => {
    const p = this.params();
    const status = p.get('status') as DashboardStatus | null;
    const sort = p.get('sort') as DashboardSort | null;
    return {
      q: p.get('q') ?? '',
      status: status && DASHBOARD_STATUSES.includes(status) ? status : 'All',
      sort: sort && DASHBOARD_SORTS.includes(sort) ? sort : 'createdDesc',
      page: Math.max(1, Number(p.get('page')) || 1),
      pageSize: PAGE_SIZE,
    };
  });

  readonly hasFilters = computed(() => this.query().q !== '' || this.query().status !== 'All');

  // --- data -----------------------------------------------------------------------
  readonly postings = httpResource<PagedResponse<JobPostingSummary>>(() => ({
    url: this.api.base,
    params: this.api.listParams(this.query()),
  }));

  readonly items = computed(() => this.postings.value()?.items ?? []);
  readonly total = computed(() => this.postings.value()?.total ?? 0);
  readonly totalPages = computed(() => this.postings.value()?.totalPages ?? 0);
  readonly publishedCount = computed(() => this.items().filter((p) => p.status === 'Published').length);

  /** Skeleton only after 200 ms, to avoid a flash on fast responses. */
  readonly showSkeleton = signal(false);
  readonly skeletonRows = [1, 2, 3, 4, 5];

  readonly announcement = computed(() => {
    if (this.postings.isLoading()) return 'Loading postings';
    if (this.postings.error()) return "Couldn't load postings";
    const n = this.total();
    return `${n} posting${n === 1 ? '' : 's'} loaded`;
  });

  readonly today = new Date();

  // --- search box (debounced 300 ms) ----------------------------------------------
  private readonly searchInput = new Subject<string>();
  readonly searchValue = signal(this.query().q);

  constructor() {
    this.searchInput.pipe(debounceTime(300), distinctUntilChanged()).subscribe((q) => this.patch({ q, page: 1 }));

    effect((onCleanup) => {
      const loading = this.postings.isLoading();
      if (!loading) {
        untracked(() => this.showSkeleton.set(false));
        return;
      }
      const timer = setTimeout(() => this.showSkeleton.set(true), 200);
      onCleanup(() => clearTimeout(timer));
    });
  }

  onSearch(value: string): void {
    this.searchValue.set(value);
    this.searchInput.next(value.trim());
  }

  setStatus(status: string): void {
    this.patch({ status, page: 1 });
  }

  setSort(sort: string): void {
    this.patch({ sort, page: 1 });
  }

  goTo(page: number): void {
    this.patch({ page });
  }

  clearFilters(searchBox: HTMLInputElement): void {
    this.searchValue.set('');
    this.patch({ q: null, status: null, page: null });
    searchBox.focus();
  }

  retry(): void {
    this.postings.reload();
  }

  closesSoon(p: JobPostingSummary): boolean {
    if (p.status !== 'Published') return false;
    const days = (new Date(p.closingDate).getTime() - this.today.getTime()) / 86_400_000;
    return days >= 0 && days <= CLOSING_SOON_DAYS;
  }

  daysUntil(date: string): number {
    return Math.max(0, Math.ceil((new Date(date).getTime() - this.today.getTime()) / 86_400_000));
  }

  pages(): number[] {
    return Array.from({ length: this.totalPages() }, (_, i) => i + 1);
  }

  private patch(params: Record<string, string | number | null>): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: params,
      queryParamsHandling: 'merge',
      replaceUrl: false,
    });
  }
}
