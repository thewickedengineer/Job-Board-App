import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ProblemDetails } from '../../core/problem-details';
import { JobPostingFormComponent } from './job-posting-form.component';

/** The exact shape the Post API returns for a 400 (CLAUDE.md §6). */
const SERVER_REJECTION: ProblemDetails = {
  type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
  title: 'One or more validation errors occurred.',
  status: 400,
  errors: {
    salaryMax: ['Salary maximum must be greater than salary minimum.'],
    closingDate: ['Closing date must be in the future.'],
    referenceCode: ['REF-4471 is already used by another of your postings.'],
    somethingUnknown: ['A key no control has.'],
  },
  traceId: '00-abc-def-00',
};

describe('JobPostingFormComponent — server error mapping', () => {
  let fixture: ComponentFixture<JobPostingFormComponent>;
  let component: JobPostingFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [JobPostingFormComponent],
      providers: [provideZonelessChangeDetection()],
    }).compileComponents();

    fixture = TestBed.createComponent(JobPostingFormComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('maps each camelCase key onto the control of the same name as a `server` error', async () => {
    component.applyProblem(SERVER_REJECTION);
    await fixture.whenStable();

    expect(component.form.controls.salaryMax.errors?.['server']).toBe('Salary maximum must be greater than salary minimum.');
    expect(component.form.controls.closingDate.errors?.['server']).toBe('Closing date must be in the future.');
    expect(component.form.controls.referenceCode.errors?.['server']).toBe('REF-4471 is already used by another of your postings.');
    expect(component.form.controls.title.errors?.['server']).toBeUndefined();
  });

  it('renders the error summary banner with every failure, linking mapped ones to their inputs', async () => {
    component.applyProblem(SERVER_REJECTION);
    await fixture.whenStable();

    const host: HTMLElement = fixture.nativeElement;
    const banner = host.querySelector<HTMLElement>('[role="alert"]');
    expect(banner).not.toBeNull();
    expect(banner!.textContent).toContain('4 problems');

    const items = Array.from(banner!.querySelectorAll('li'));
    expect(items).toHaveLength(4);

    const links = Array.from(banner!.querySelectorAll('a')).map((a) => a.getAttribute('href'));
    expect(links).toEqual(['#salaryMax', '#closingDate', '#referenceCode']);

    // Labels are human, not the raw keys; the unknown key falls through unswallowed.
    expect(items[0].textContent).toContain('Salary max');
    expect(items[3].textContent).toContain('somethingUnknown');
    expect(items[3].querySelector('a')).toBeNull();
  });

  it('shows the server message inline under the field and marks it invalid', async () => {
    component.applyProblem(SERVER_REJECTION);
    await fixture.whenStable();

    const host: HTMLElement = fixture.nativeElement;
    const salaryMax = host.querySelector<HTMLInputElement>('#salaryMax')!;
    expect(salaryMax.getAttribute('aria-invalid')).toBe('true');
    expect(host.querySelector('#salaryMax-error')?.textContent).toContain('Salary maximum must be greater than salary minimum.');
  });

  it('clears a server error on the next change to that control', async () => {
    component.applyProblem(SERVER_REJECTION);
    component.form.controls.closingDate.setValue('2099-01-01');
    await fixture.whenStable();

    expect(component.form.controls.closingDate.errors?.['server']).toBeUndefined();
    // The other server errors are untouched until their own controls change.
    expect(component.form.controls.salaryMax.errors?.['server']).toBeDefined();
  });

  it('surfaces the client cross-field salary rule against salaryMax before anything is sent', async () => {
    const emitted: unknown[] = [];
    component.submitted.subscribe((s) => emitted.push(s));

    component.form.patchValue({ salaryMin: 46_000, salaryMax: 38_000 });
    component.submit('publish');
    await fixture.whenStable();

    expect(emitted).toHaveLength(0);
    expect(component.form.hasError('salaryRange')).toBe(true);
    expect(component.errorFor('salaryMax')).toBe('Salary maximum must be greater than salary minimum.');
    const banner: HTMLElement = fixture.nativeElement.querySelector('[role="alert"]');
    expect(banner.textContent).toContain('Salary max');
    expect(banner.textContent).toContain('Closing date');
  });
});
