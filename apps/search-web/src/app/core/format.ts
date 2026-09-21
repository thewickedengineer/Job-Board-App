import { label } from './models';

const CURRENCY_SYMBOL: Record<string, string> = { GBP: '£', USD: '$', CAD: '$', AUD: '$', EUR: '€', INR: '₹' };
const DAY = 86_400_000;

/** "£38,000 – £46,000 / year", or the not-disclosed copy when the manager hid it. */
export function salaryText(job: { salaryMin: number | null; salaryMax: number | null; salaryCurrency: string; payPeriod: string; salaryVisible: boolean }): string {
  if (!job.salaryVisible || job.salaryMin === null || job.salaryMax === null) {
    return 'Salary not disclosed';
  }
  const symbol = CURRENCY_SYMBOL[job.salaryCurrency] ?? `${job.salaryCurrency} `;
  const fmt = (n: number) => `${symbol}${Math.round(n).toLocaleString('en-GB')}`;
  const suffix = job.salaryCurrency === 'CAD' || job.salaryCurrency === 'AUD' ? ` ${job.salaryCurrency}` : '';
  return `${fmt(job.salaryMin)} – ${fmt(job.salaryMax)}${suffix} / ${label(job.payPeriod)}`;
}

/** Compact form for chips and similar-role rows: "£35k–£39k". */
export function salaryCompact(job: { salaryMin: number | null; salaryMax: number | null; salaryCurrency: string; salaryVisible: boolean }): string {
  if (!job.salaryVisible || job.salaryMin === null || job.salaryMax === null) {
    return 'Salary undisclosed';
  }
  const symbol = CURRENCY_SYMBOL[job.salaryCurrency] ?? '';
  const k = (n: number) => (n >= 1000 ? `${Math.round(n / 1000)}k` : `${n}`);
  return `${symbol}${k(job.salaryMin)}–${symbol}${k(job.salaryMax)}`;
}

/** "Posted today" · "Posted 2 days ago" · "Posted 3 weeks ago". */
export function postedAgo(publishedAt: string, now = new Date()): string {
  const days = Math.floor((now.getTime() - new Date(publishedAt).getTime()) / DAY);
  if (days <= 0) return 'Posted today';
  if (days === 1) return 'Posted yesterday';
  if (days < 14) return `Posted ${days} days ago`;
  if (days < 60) return `Posted ${Math.floor(days / 7)} weeks ago`;
  return `Posted ${Math.floor(days / 30)} months ago`;
}

/** Whole days until the closing date (date-only, UTC), negative when past. */
export function daysUntil(closingDate: string, now = new Date()): number {
  const today = Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate());
  const [y, m, d] = closingDate.split('-').map(Number);
  return Math.round((Date.UTC(y, m - 1, d) - today) / DAY);
}

export const CLOSING_SOON_DAYS = 7;

export function closesText(closingDate: string, now = new Date()): string {
  const days = daysUntil(closingDate, now);
  if (days < 0) return `Closed ${formatDate(closingDate)}`;
  if (days === 0) return 'Closes today';
  if (days === 1) return 'Closes tomorrow';
  if (days <= CLOSING_SOON_DAYS) return `Closes in ${days} days`;
  return `Closes ${formatDate(closingDate)}`;
}

export function closesSoon(closingDate: string, now = new Date()): boolean {
  const days = daysUntil(closingDate, now);
  return days >= 0 && days <= CLOSING_SOON_DAYS;
}

export function formatDate(iso: string): string {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d)).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' });
}

/** "One per line" text → list items, blank lines dropped. */
export function lines(text: string | null): string[] {
  return (text ?? '').split(/\r?\n/).map((l) => l.replace(/^[-•*]\s*/, '').trim()).filter(Boolean);
}
