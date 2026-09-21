import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { JobsService } from './jobs.service';
import { EMPTY_FILTERS, JobFilters } from './models';

/** The URL is the source of truth for the results page: state → params → state must be lossless. */
describe('JobsService URL mapping', () => {
  let service: JobsService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient()] });
    service = TestBed.inject(JobsService);
  });

  const filters: JobFilters = {
    q: 'warehouse supervisor',
    department: ['Operations', 'Transport'],
    location: 'Leeds',
    workArrangement: ['Hybrid'],
    employmentType: [],
    seniority: ['Senior'],
    salaryMin: 35_000,
    salaryMax: 60_000,
    postedWithinDays: 30,
    sort: 'salaryDesc',
    page: 3,
  };

  it('round-trips every filter through query params', () => {
    const params = service.toQueryParams(filters);
    const get = (k: string) => { const v = params[k]; return v === null || v === undefined ? null : Array.isArray(v) ? v[0] : String(v); };
    const getAll = (k: string) => { const v = params[k]; return v === null || v === undefined ? [] : Array.isArray(v) ? v : [String(v)]; };

    expect(service.fromQueryParams(get, getAll)).toEqual(filters);
  });

  it('omits defaults so shared URLs stay short', () => {
    const params = service.toQueryParams(EMPTY_FILTERS);
    expect(Object.values(params).every((v) => v === null)).toBe(true);
  });

  it('ignores unknown sorts and garbage numbers instead of failing', () => {
    const state = service.fromQueryParams((k) => ({ sort: 'newest', page: '-4', salaryMin: 'abc' })[k] ?? null, () => []);
    expect(state.sort).toBe('recent');
    expect(state.page).toBe(1);
    expect(state.salaryMin).toBeNull();
  });

  it('sends multi-select dimensions as repeated keys and caps nothing client-side', () => {
    const p = service.listParams(filters);
    expect(p.getAll('department')).toEqual(['Operations', 'Transport']);
    expect(p.get('sort')).toBe('salaryDesc');
    expect(p.get('page')).toBe('3');
    expect(service.facetParams(filters).has('page')).toBe(false);
    expect(service.facetParams(filters).has('sort')).toBe(false);
  });
});
