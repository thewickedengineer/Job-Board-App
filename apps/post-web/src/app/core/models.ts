// DTOs mirrored from the Post API. Duplicated (not shared) with search-web by design.

export interface ManagerResponse {
  id: string;
  email: string;
  fullName: string;
  organization: string;
  emailVerified: boolean;
  createdAt: string;
  lastLoginAt: string | null;
}

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  manager: ManagerResponse;
}

export interface TokenPairResponse {
  accessToken: string;
  refreshToken: string;
}

export type JobPostingStatus = 'Draft' | 'Published' | 'Closed' | 'Expired';

export const EMPLOYMENT_TYPES = ['FullTime', 'PartTime', 'Contract', 'Temporary', 'Internship'] as const;
export const SENIORITIES = ['Intern', 'Junior', 'Mid', 'Senior', 'Lead', 'Principal', 'Director', 'Executive'] as const;
export const WORK_ARRANGEMENTS = ['OnSite', 'Hybrid', 'Remote'] as const;
export const PAY_PERIODS = ['Annual', 'Monthly', 'Hourly'] as const;
export const CURRENCIES = ['CAD', 'USD', 'GBP', 'EUR', 'AUD', 'INR'] as const;

export type EmploymentType = (typeof EMPLOYMENT_TYPES)[number];
export type Seniority = (typeof SENIORITIES)[number];
export type WorkArrangement = (typeof WORK_ARRANGEMENTS)[number];
export type PayPeriod = (typeof PAY_PERIODS)[number];
export type Currency = (typeof CURRENCIES)[number];

/** Human labels for the closed vocabularies. Keys are the wire values. */
export const LABELS = {
  employmentType: { FullTime: 'Full-time', PartTime: 'Part-time', Contract: 'Contract', Temporary: 'Temporary', Internship: 'Internship' },
  workArrangement: { OnSite: 'On-site', Hybrid: 'Hybrid', Remote: 'Remote' },
  payPeriod: { Annual: 'year', Monthly: 'month', Hourly: 'hour' },
  currency: { CAD: 'CAD ($)', USD: 'USD ($)', GBP: 'GBP (£)', EUR: 'EUR (€)', AUD: 'AUD ($)', INR: 'INR (₹)' },
} as const;

/** What the manager submits. Field names equal the server's validation keys. */
export interface JobPostingRequest {
  referenceCode: string | null;
  title: string;
  department: string;
  employmentType: EmploymentType;
  seniority: Seniority;
  openings: number;
  workArrangement: WorkArrangement;
  location: string | null;
  country: string;
  salaryMin: number;
  salaryMax: number;
  salaryCurrency: Currency;
  payPeriod: PayPeriod;
  salaryVisible: boolean;
  description: string;
  responsibilities: string | null;
  requirements: string | null;
  skills: string[];
  applicationUrl: string | null;
  applicationEmail: string | null;
  closingDate: string; // yyyy-MM-dd
  status: 'Draft' | 'Published';
}

export interface UpdateJobPostingRequest extends JobPostingRequest {
  version: number;
}

/** The complete persisted record, exactly as the API returns it. */
export interface JobPostingResponse extends Omit<JobPostingRequest, 'status'> {
  id: string;
  slug: string;
  status: JobPostingStatus;
  isOpen: boolean;
  location: string;
  createdAt: string;
  updatedAt: string;
  publishedAt: string | null;
  version: number;
}

export interface JobPostingSummary {
  id: string;
  slug: string;
  status: JobPostingStatus;
  referenceCode: string | null;
  title: string;
  department: string;
  location: string;
  workArrangement: WorkArrangement;
  closingDate: string;
  createdAt: string;
  updatedAt: string;
}

export interface PagedResponse<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
  totalPages: number;
}

export const DASHBOARD_SORTS = ['createdDesc', 'createdAsc', 'closingAsc', 'closingDesc', 'titleAsc', 'titleDesc'] as const;
export type DashboardSort = (typeof DASHBOARD_SORTS)[number];
export const DASHBOARD_STATUSES = ['All', 'Published', 'Draft', 'Closed', 'Expired'] as const;
export type DashboardStatus = (typeof DASHBOARD_STATUSES)[number];
