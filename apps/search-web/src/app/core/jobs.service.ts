import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { EMPTY_FILTERS, JobDetail, JobFilters, PAGE_SIZE, SORTS, Sort } from './models';

/** Query-string ⇄ filter state. The URL is the source of truth for the results page. */
@Injectable({ providedIn: 'root' })
export class JobsService {
  private readonly http = inject(HttpClient);
  readonly base = `${environment.apiBaseUrl}/api`;

  detail(slug: string): Observable<JobDetail> {
    return this.http.get<JobDetail>(`${this.base}/jobs/${encodeURIComponent(slug)}`);
  }

  /** Params for GET /api/jobs. */
  listParams(f: JobFilters): HttpParams {
    let p = this.facetParams(f).set('sort', f.sort).set('page', f.page).set('pageSize', PAGE_SIZE);
    return p;
  }

  /** Params for GET /api/facets — everything except sort and page. */
  facetParams(f: JobFilters): HttpParams {
    let p = new HttpParams();
    if (f.q.trim()) p = p.set('q', f.q.trim());
    if (f.location.trim()) p = p.set('location', f.location.trim());
    for (const d of f.department) p = p.append('department', d);
    for (const w of f.workArrangement) p = p.append('workArrangement', w);
    for (const e of f.employmentType) p = p.append('employmentType', e);
    for (const s of f.seniority) p = p.append('seniority', s);
    if (f.salaryMin !== null) p = p.set('salaryMin', f.salaryMin);
    if (f.salaryMax !== null) p = p.set('salaryMax', f.salaryMax);
    if (f.postedWithinDays !== null) p = p.set('postedWithinDays', f.postedWithinDays);
    return p;
  }

  /** Router query params for the same state; empty values are omitted so URLs stay short. */
  toQueryParams(f: JobFilters): Record<string, string | string[] | number | null> {
    return {
      q: f.q.trim() || null,
      department: f.department.length ? f.department : null,
      location: f.location.trim() || null,
      workArrangement: f.workArrangement.length ? f.workArrangement : null,
      employmentType: f.employmentType.length ? f.employmentType : null,
      seniority: f.seniority.length ? f.seniority : null,
      salaryMin: f.salaryMin,
      salaryMax: f.salaryMax,
      postedWithinDays: f.postedWithinDays,
      sort: f.sort === 'recent' ? null : f.sort,
      page: f.page > 1 ? f.page : null,
    };
  }

  fromQueryParams(get: (key: string) => string | null, getAll: (key: string) => string[]): JobFilters {
    const num = (key: string) => {
      const v = Number(get(key));
      return get(key) !== null && Number.isFinite(v) && v >= 0 ? v : null;
    };
    const sort = get('sort') as Sort | null;
    return {
      ...EMPTY_FILTERS,
      q: get('q') ?? '',
      location: get('location') ?? '',
      department: getAll('department'),
      workArrangement: getAll('workArrangement'),
      employmentType: getAll('employmentType'),
      seniority: getAll('seniority'),
      salaryMin: num('salaryMin'),
      salaryMax: num('salaryMax'),
      postedWithinDays: num('postedWithinDays'),
      sort: sort && SORTS.includes(sort) ? sort : 'recent',
      page: Math.max(1, Math.floor(num('page') ?? 1)),
    };
  }
}
