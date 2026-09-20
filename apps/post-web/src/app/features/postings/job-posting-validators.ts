import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

/**
 * Client mirrors of CLAUDE.md §6. They exist for latency only; the server is
 * the authority and its messages win when they arrive. Keys are the same the
 * server uses so a template can render either source the same way.
 */
export const MAX_SALARY = 10_000_000;
export const MAX_SKILLS = 20;

export function todayUtc(): string {
  return new Date().toISOString().slice(0, 10);
}

/** yyyy-mm-dd strictly after today's UTC date. */
export const futureDate: ValidatorFn = (control) => {
  const value = control.value as string | null;
  if (!value) return null;
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || Number.isNaN(Date.parse(value))) return { dateFormat: true };
  return value > todayUtc() ? null : { future: true };
};

/** Group-level: min strictly below max, only once both are individually valid. Surfaced against salaryMax. */
export const salaryRange: ValidatorFn = (group) => {
  const min = group.get('salaryMin')?.value as number | null;
  const max = group.get('salaryMax')?.value as number | null;
  const valid = (n: number | null): n is number => typeof n === 'number' && n > 0 && n <= MAX_SALARY;
  return valid(min) && valid(max) && min >= max ? { salaryRange: 'Salary maximum must be greater than salary minimum.' } : null;
};

/** Location is required unless the arrangement is Remote (then the country stands in). */
export const locationUnlessRemote: ValidatorFn = (control) => {
  const arrangement = control.parent?.get('workArrangement')?.value as string | undefined;
  const value = ((control.value as string | null) ?? '').trim();
  if (arrangement !== 'Remote' && value.length === 0) return { required: true };
  if (value.length > 0 && (value.length < 2 || value.length > 120)) return { length: { min: 2, max: 120 } };
  return null;
};

export const skillsList: ValidatorFn = (control) => {
  const skills = (control.value as string[] | null) ?? [];
  if (skills.length > MAX_SKILLS) return { maxSkills: true };
  if (skills.some((s) => s.trim().length < 1 || s.trim().length > 40)) return { skillLength: true };
  return null;
};

export const absoluteHttpUrl: ValidatorFn = (control) => {
  const value = ((control.value as string | null) ?? '').trim();
  if (!value) return null;
  try {
    const url = new URL(value);
    return (url.protocol === 'http:' || url.protocol === 'https:') && url.hostname ? null : { url: true };
  } catch {
    return { url: true };
  }
};

export const emailAddress: ValidatorFn = (control) => {
  const value = ((control.value as string | null) ?? '').trim();
  if (!value) return null;
  return /^[^@\s]+@[^@\s]+\.[^@\s.]+$/.test(value) && value.length <= 320 ? null : { email: true };
};

export const wholeNumber: ValidatorFn = (control) => {
  const value = control.value as number | null;
  return value === null || Number.isInteger(value) ? null : { integer: true };
};

/** Human message for whichever error a control (or the group, for salaryMax) carries. */
export function messageFor(name: string, errors: ValidationErrors | null): string | null {
  if (!errors) return null;
  if (typeof errors['server'] === 'string') return errors['server'];
  if (typeof errors['salaryRange'] === 'string') return errors['salaryRange'];
  const label = FIELD_LABELS[name] ?? name;
  if (errors['required']) return `${label} is required.`;
  if (errors['minlength']) return `${label} must be at least ${errors['minlength'].requiredLength} characters.`;
  if (errors['maxlength']) return `${label} must be at most ${errors['maxlength'].requiredLength.toLocaleString()} characters.`;
  if (errors['length']) return `${label} must be ${errors['length'].min}–${errors['length'].max} characters.`;
  if (errors['min']) return `${label} must be greater than ${errors['min'].min - 1 < 0 ? 0 : errors['min'].min - 1}.`;
  if (errors['max']) return `${label} must be at most ${errors['max'].max.toLocaleString()}.`;
  if (errors['integer']) return `${label} must be a whole number.`;
  if (errors['future']) return 'Closing date must be in the future.';
  if (errors['dateFormat']) return 'Enter the closing date as yyyy-mm-dd.';
  if (errors['maxSkills']) return `Add at most ${MAX_SKILLS} skills.`;
  if (errors['skillLength']) return 'Each skill must be 1–40 characters.';
  if (errors['url']) return 'Application URL must be an absolute http:// or https:// link.';
  if (errors['email']) return 'Enter a valid application email address.';
  return 'Enter a valid value.';
}

export const FIELD_LABELS: Record<string, string> = {
  referenceCode: 'Internal reference code',
  title: 'Job title',
  department: 'Department',
  employmentType: 'Employment type',
  seniority: 'Seniority',
  openings: 'Number of openings',
  workArrangement: 'Work arrangement',
  location: 'Location',
  country: 'Country',
  salaryMin: 'Salary min',
  salaryMax: 'Salary max',
  salaryCurrency: 'Currency',
  payPeriod: 'Pay period',
  salaryVisible: 'Salary visibility',
  description: 'Job description',
  responsibilities: 'Responsibilities',
  requirements: 'Requirements',
  skills: 'Skills',
  applicationUrl: 'Application URL',
  applicationEmail: 'Application email',
  closingDate: 'Closing date',
  status: 'Status',
  version: 'Version',
};
