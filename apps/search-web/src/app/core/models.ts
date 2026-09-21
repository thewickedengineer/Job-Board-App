// DTOs mirrored from the Search API. Duplicated (not shared) with post-web by design.

export interface JobSummary {
  id: string;
  slug: string;
  title: string;
  department: string;
  location: string;
  country: string;
  workArrangement: string;
  employmentType: string;
  seniority: string;
  salaryMin: number | null;
  salaryMax: number | null;
  salaryCurrency: string;
  payPeriod: string;
  salaryVisible: boolean;
  excerpt: string;
  organization: string;
  closingDate: string;
  publishedAt: string;
}

export interface SimilarJob {
  slug: string;
  title: string;
  department: string;
  location: string;
  workArrangement: string;
  employmentType: string;
  salaryMin: number | null;
  salaryMax: number | null;
  salaryCurrency: string;
  payPeriod: string;
  salaryVisible: boolean;
}

export interface JobDetail extends Omit<JobSummary, 'excerpt'> {
  description: string;
  responsibilities: string | null;
  requirements: string | null;
  skills: string[];
  applicationUrl: string | null;
  applicationEmail: string | null;
  isOpen: boolean;
  similar: SimilarJob[];
}

export interface PagedResponse<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
  totalPages: number;
}

export interface FacetValue {
  value: string;
  count: number;
}

export interface Facets {
  departments: FacetValue[];
  locations: FacetValue[];
  workArrangements: FacetValue[];
  employmentTypes: FacetValue[];
  seniorities: FacetValue[];
  total: number;
}

export const SORTS = ['recent', 'relevance', 'salaryDesc', 'salaryAsc', 'closingSoon'] as const;
export type Sort = (typeof SORTS)[number];
export const SORT_LABELS: Record<Sort, string> = {
  recent: 'Most recent',
  relevance: 'Best match',
  salaryDesc: 'Salary: high to low',
  salaryAsc: 'Salary: low to high',
  closingSoon: 'Closing soon',
};

export const POSTED_WITHIN = [
  { value: null, label: 'Any time' },
  { value: 1, label: 'Last 24 hours' },
  { value: 7, label: 'Last 7 days' },
  { value: 30, label: 'Last 30 days' },
] as const;

export const SENIORITIES = ['Intern', 'Junior', 'Mid', 'Senior', 'Lead', 'Principal', 'Director', 'Executive'] as const;

/** Human labels for wire values. */
export const LABELS: Record<string, string> = {
  FullTime: 'Full-time',
  PartTime: 'Part-time',
  Contract: 'Contract',
  Temporary: 'Temporary',
  Internship: 'Internship',
  OnSite: 'On-site',
  Hybrid: 'Hybrid',
  Remote: 'Remote',
  Annual: 'year',
  Monthly: 'month',
  Hourly: 'hour',
};

export function label(value: string): string {
  return LABELS[value] ?? value;
}

/** The candidate's filter state — every field maps 1:1 onto a URL query param. */
export interface JobFilters {
  q: string;
  department: string[];
  location: string;
  workArrangement: string[];
  employmentType: string[];
  seniority: string[];
  salaryMin: number | null;
  salaryMax: number | null;
  postedWithinDays: number | null;
  sort: Sort;
  page: number;
}

export const EMPTY_FILTERS: JobFilters = {
  q: '',
  department: [],
  location: '',
  workArrangement: [],
  employmentType: [],
  seniority: [],
  salaryMin: null,
  salaryMax: null,
  postedWithinDays: null,
  sort: 'recent',
  page: 1,
};

export const PAGE_SIZE = 10;
export const SALARY_SLIDER_MAX = 200_000;
