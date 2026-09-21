import { closesSoon, closesText, daysUntil, lines, postedAgo, salaryCompact, salaryText } from './format';

const now = new Date('2026-09-20T12:00:00Z');

describe('format helpers (client-derived copy from wireframe 2.1/2.3)', () => {
  it('assembles the salary string from the numeric parts', () => {
    expect(salaryText({ salaryMin: 38_000, salaryMax: 46_000, salaryCurrency: 'GBP', payPeriod: 'Annual', salaryVisible: true })).toBe('£38,000 – £46,000 / year');
    expect(salaryText({ salaryMin: 22, salaryMax: 28, salaryCurrency: 'CAD', payPeriod: 'Hourly', salaryVisible: true })).toBe('$22 – $28 CAD / hour');
    expect(salaryCompact({ salaryMin: 35_000, salaryMax: 39_000, salaryCurrency: 'GBP', salaryVisible: true })).toBe('£35k–£39k');
  });

  it('never shows numbers for a hidden salary', () => {
    expect(salaryText({ salaryMin: null, salaryMax: null, salaryCurrency: 'GBP', payPeriod: 'Annual', salaryVisible: false })).toBe('Salary not disclosed');
    expect(salaryCompact({ salaryMin: null, salaryMax: null, salaryCurrency: 'GBP', salaryVisible: false })).toBe('Salary undisclosed');
  });

  it('describes when a posting went up', () => {
    expect(postedAgo('2026-09-20T09:00:00Z', now)).toBe('Posted today');
    expect(postedAgo('2026-09-19T09:00:00Z', now)).toBe('Posted yesterday');
    expect(postedAgo('2026-09-18T09:00:00Z', now)).toBe('Posted 2 days ago');
    expect(postedAgo('2026-08-25T09:00:00Z', now)).toBe('Posted 3 weeks ago');
  });

  it('flags closing within seven days and phrases the deadline', () => {
    expect(daysUntil('2026-09-25', now)).toBe(5);
    expect(closesText('2026-09-25', now)).toBe('Closes in 5 days');
    expect(closesSoon('2026-09-25', now)).toBe(true);
    expect(closesSoon('2026-09-28', now)).toBe(false);
    expect(closesText('2026-09-20', now)).toBe('Closes today');
    expect(closesText('2026-10-23', now)).toBe('Closes 23 Oct 2026');
    expect(closesText('2026-08-11', now)).toBe('Closed 11 Aug 2026');
  });

  it('turns one-per-line text into list items and drops blanks and bullets', () => {
    expect(lines('Lead a team of 14\n\n- Hold 99.2% pick accuracy\n• Weekly one-to-ones\n')).toEqual(['Lead a team of 14', 'Hold 99.2% pick accuracy', 'Weekly one-to-ones']);
    expect(lines(null)).toEqual([]);
  });
});
