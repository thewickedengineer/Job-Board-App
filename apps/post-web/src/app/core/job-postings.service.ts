import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  DashboardSort,
  DashboardStatus,
  JobPostingRequest,
  JobPostingResponse,
  JobPostingSummary,
  PagedResponse,
  UpdateJobPostingRequest,
} from './models';

export interface DashboardQuery {
  q: string;
  status: DashboardStatus;
  sort: DashboardSort;
  page: number;
  pageSize: number;
}

/** Typed access to /api/job-postings. The interceptor adds the bearer token. */
@Injectable({ providedIn: 'root' })
export class JobPostingsService {
  private readonly http = inject(HttpClient);
  readonly base = `${environment.apiBaseUrl}/api/job-postings`;

  listParams(query: DashboardQuery): HttpParams {
    let params = new HttpParams()
      .set('page', query.page)
      .set('pageSize', query.pageSize)
      .set('sort', query.sort)
      .set('status', query.status);
    if (query.q.trim()) {
      params = params.set('q', query.q.trim());
    }
    return params;
  }

  list(query: DashboardQuery): Observable<PagedResponse<JobPostingSummary>> {
    return this.http.get<PagedResponse<JobPostingSummary>>(this.base, { params: this.listParams(query) });
  }

  get(id: string): Observable<JobPostingResponse> {
    return this.http.get<JobPostingResponse>(`${this.base}/${id}`);
  }

  create(request: JobPostingRequest): Observable<JobPostingResponse> {
    return this.http.post<JobPostingResponse>(this.base, request);
  }

  update(id: string, request: UpdateJobPostingRequest): Observable<JobPostingResponse> {
    return this.http.put<JobPostingResponse>(`${this.base}/${id}`, request);
  }

  close(id: string): Observable<JobPostingResponse> {
    return this.http.post<JobPostingResponse>(`${this.base}/${id}/close`, null);
  }
}
