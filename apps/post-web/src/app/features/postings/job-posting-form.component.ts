import { ChangeDetectionStrategy, Component, Injector, computed, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { focusAfterRender } from '../../core/focus';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { map, startWith } from 'rxjs';
import {
  CURRENCIES,
  Currency,
  EMPLOYMENT_TYPES,
  EmploymentType,
  JobPostingRequest,
  JobPostingResponse,
  LABELS,
  PAY_PERIODS,
  PayPeriod,
  SENIORITIES,
  Seniority,
  WORK_ARRANGEMENTS,
  WorkArrangement,
} from '../../core/models';
import { ProblemDetails, applyServerErrors } from '../../core/problem-details';
import { ChipInputComponent } from '../../shared/chip-input.component';
import { DatePickerComponent } from '../../shared/date-picker.component';
import { ErrorSummaryComponent, SummaryEntry, bannerOf } from '../../shared/error-summary.component';
import {
  FIELD_LABELS,
  MAX_SALARY,
  MAX_SKILLS,
  absoluteHttpUrl,
  emailAddress,
  futureDate,
  locationUnlessRemote,
  messageFor,
  salaryRange,
  skillsList,
  todayUtc,
  wholeNumber,
} from './job-posting-validators';

export type SubmitIntent = 'draft' | 'publish' | 'save';

export interface FormSubmission {
  request: JobPostingRequest;
  intent: SubmitIntent;
}

const DESCRIPTION_MAX = 10_000;

/**
 * Wireframe 1.4 / 1.4b. One typed reactive form used by create and edit.
 * Control names equal the API's validation keys and every input's id equals
 * its control name, so server errors map onto fields and the summary banner
 * links to them with no translation table.
 */
@Component({
  selector: 'app-job-posting-form',
  imports: [ReactiveFormsModule, ErrorSummaryComponent, ChipInputComponent, DatePickerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './job-posting-form.component.html',
  styleUrl: './job-posting-form.component.scss',
})
export class JobPostingFormComponent {
  readonly mode = input<'create' | 'edit'>('create');
  readonly initial = input<JobPostingResponse | null>(null);
  readonly submitting = input(false);
  readonly submitted = output<FormSubmission>();
  readonly cancelled = output<void>();

  private readonly summary = viewChild.required(ErrorSummaryComponent);
  private readonly injector = inject(Injector);

  readonly employmentTypes = EMPLOYMENT_TYPES;
  readonly seniorities = SENIORITIES;
  readonly workArrangements = WORK_ARRANGEMENTS;
  readonly payPeriods = PAY_PERIODS;
  readonly currencies = CURRENCIES;
  readonly labels = LABELS;
  readonly maxSkills = MAX_SKILLS;
  readonly descriptionMax = DESCRIPTION_MAX;
  readonly minClosingDate = computed(() => {
    const t = new Date(todayUtc());
    t.setUTCDate(t.getUTCDate() + 1);
    return t.toISOString().slice(0, 10);
  });

  readonly form = new FormGroup(
    {
      referenceCode: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(40)] }),
      title: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(3), Validators.maxLength(120)] }),
      department: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(2), Validators.maxLength(80)] }),
      employmentType: new FormControl<EmploymentType>('FullTime', { nonNullable: true, validators: [Validators.required] }),
      seniority: new FormControl<Seniority>('Mid', { nonNullable: true, validators: [Validators.required] }),
      openings: new FormControl<number | null>(1, { validators: [Validators.required, Validators.min(1), Validators.max(999), wholeNumber] }),
      workArrangement: new FormControl<WorkArrangement>('OnSite', { nonNullable: true, validators: [Validators.required] }),
      location: new FormControl('', { nonNullable: true, validators: [locationUnlessRemote] }),
      country: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(2), Validators.maxLength(80)] }),
      salaryMin: new FormControl<number | null>(null, { validators: [Validators.required, Validators.min(0.01), Validators.max(MAX_SALARY)] }),
      salaryMax: new FormControl<number | null>(null, { validators: [Validators.required, Validators.min(0.01), Validators.max(MAX_SALARY)] }),
      salaryCurrency: new FormControl<Currency>('CAD', { nonNullable: true, validators: [Validators.required] }),
      payPeriod: new FormControl<PayPeriod>('Annual', { nonNullable: true, validators: [Validators.required] }),
      salaryVisible: new FormControl(true, { nonNullable: true }),
      description: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(50), Validators.maxLength(DESCRIPTION_MAX)] }),
      responsibilities: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(DESCRIPTION_MAX)] }),
      requirements: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(DESCRIPTION_MAX)] }),
      skills: new FormControl<string[]>([], { nonNullable: true, validators: [skillsList] }),
      applicationUrl: new FormControl('', { nonNullable: true, validators: [absoluteHttpUrl, Validators.maxLength(2048)] }),
      applicationEmail: new FormControl('', { nonNullable: true, validators: [emailAddress] }),
      closingDate: new FormControl('', { nonNullable: true, validators: [Validators.required, futureDate] }),
    },
    { validators: [salaryRange] },
  );

  /** Whatever is in the banner: client failures on submit, or server failures after it. */
  readonly errors = signal<SummaryEntry[]>([]);
  readonly bannerTitle = computed(() => {
    const n = this.errors().length;
    return this.mode() === 'edit'
      ? `We couldn't save this posting — ${n} problem${n === 1 ? '' : 's'}`
      : `We couldn't publish this posting — ${n} problem${n === 1 ? '' : 's'}`;
  });

  private readonly value = toSignal(this.form.valueChanges.pipe(startWith(null), map(() => this.form.getRawValue())), { initialValue: this.form.getRawValue() });
  readonly dirty = toSignal(this.form.valueChanges.pipe(map(() => this.form.dirty)), { initialValue: false });
  readonly isRemote = computed(() => this.value().workArrangement === 'Remote');
  readonly descriptionLength = computed(() => this.value().description.length);
  readonly isDraft = computed(() => this.initial()?.status === 'Draft');
  readonly isClosed = computed(() => this.initial()?.status === 'Closed');

  /** Polite live region text — updated only at 90 % and at the limit (wireframe 1.4). */
  readonly counterAnnouncement = signal('');

  constructor() {
    // Hydrate from the record we were given (edit) and lock a closed posting.
    effect(() => {
      const initial = this.initial();
      untracked(() => {
        if (initial) {
          this.form.reset(this.fromResponse(initial));
          if (initial.status === 'Closed' || initial.status === 'Expired') {
            this.form.disable();
          }
        }
      });
    });

    // Remote relabels Location and drops its "required"; re-validate when it flips.
    this.form.controls.workArrangement.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this.form.controls.location.updateValueAndValidity();
    });

    // Description counter thresholds.
    let lastBand = 0;
    this.form.controls.description.valueChanges.pipe(takeUntilDestroyed()).subscribe((text) => {
      const band = text.length >= DESCRIPTION_MAX ? 2 : text.length >= DESCRIPTION_MAX * 0.9 ? 1 : 0;
      if (band !== lastBand) {
        lastBand = band;
        this.counterAnnouncement.set(band === 2 ? 'Description is at the 10,000 character limit.' : band === 1 ? 'Description is at 90% of the limit.' : '');
      }
    });
  }

  // --- template helpers --------------------------------------------------------------

  showError(name: keyof typeof this.form.controls): boolean {
    const control = this.form.controls[name];
    if (name === 'salaryMax' && this.form.hasError('salaryRange') && (control.touched || this.form.controls.salaryMin.touched)) return true;
    return control.invalid && control.touched;
  }

  errorFor(name: keyof typeof this.form.controls): string | null {
    const control = this.form.controls[name];
    if (control.errors) return messageFor(name, control.errors);
    if (name === 'salaryMax') return messageFor(name, this.form.errors);
    return null;
  }

  describedBy(name: keyof typeof this.form.controls, hint = false): string {
    const ids = [];
    if (hint) ids.push(`${name}-hint`);
    if (this.showError(name)) ids.push(`${name}-error`);
    return ids.join(' ');
  }

  // --- submit ----------------------------------------------------------------------------

  submit(intent: SubmitIntent): void {
    if (this.submitting()) return;
    this.form.markAllAsTouched();
    this.form.updateValueAndValidity();

    if (this.form.invalid) {
      // Publish runs a full client pass first and scrolls to the first error.
      const entries: SummaryEntry[] = [];
      for (const name of Object.keys(this.form.controls) as (keyof typeof this.form.controls)[]) {
        const message = this.errorFor(name);
        if (message && (this.form.controls[name].invalid || (name === 'salaryMax' && this.form.hasError('salaryRange')))) {
          entries.push({ targetId: name, label: FIELD_LABELS[name], message });
        }
      }
      this.errors.set(entries);
      focusAfterRender(this.injector, () => {
        const first = entries[0]?.targetId;
        if (first) document.getElementById(first)?.scrollIntoView?.({ behavior: 'smooth', block: 'center' });
        return bannerOf(this.summary());
      });
      return;
    }

    this.errors.set([]);
    this.submitted.emit({ request: this.toRequest(intent === 'draft' ? 'Draft' : intent === 'publish' ? 'Published' : this.isDraft() ? 'Draft' : 'Published'), intent });
  }

  /** Server rejected: map onto controls, list everything in the banner, focus it. */
  applyProblem(problem: ProblemDetails): void {
    const entries = applyServerErrors(this.form, problem).map<SummaryEntry>((e) => ({
      targetId: e.control,
      label: FIELD_LABELS[e.key] ?? e.key,
      message: e.message,
    }));
    this.errors.set(entries);
    focusAfterRender(this.injector, () => bannerOf(this.summary()));
  }

  /** A generic (non-validation) failure for the banner. */
  fail(label: string, message: string): void {
    this.errors.set([{ targetId: null, label, message }]);
    focusAfterRender(this.injector, () => bannerOf(this.summary()));
  }

  clearErrors(): void {
    this.errors.set([]);
  }

  markPristine(): void {
    this.form.markAsPristine();
  }

  private toRequest(status: 'Draft' | 'Published'): JobPostingRequest {
    const v = this.form.getRawValue();
    const blank = (s: string) => (s.trim() ? s.trim() : null);
    return {
      referenceCode: blank(v.referenceCode),
      title: v.title.trim(),
      department: v.department.trim(),
      employmentType: v.employmentType,
      seniority: v.seniority,
      openings: v.openings ?? 1,
      workArrangement: v.workArrangement,
      location: blank(v.location),
      country: v.country.trim(),
      salaryMin: v.salaryMin ?? 0,
      salaryMax: v.salaryMax ?? 0,
      salaryCurrency: v.salaryCurrency,
      payPeriod: v.payPeriod,
      salaryVisible: v.salaryVisible,
      description: v.description.trim(),
      responsibilities: blank(v.responsibilities),
      requirements: blank(v.requirements),
      skills: v.skills,
      applicationUrl: blank(v.applicationUrl),
      applicationEmail: blank(v.applicationEmail),
      closingDate: v.closingDate,
      status,
    };
  }

  private fromResponse(r: JobPostingResponse) {
    return {
      referenceCode: r.referenceCode ?? '',
      title: r.title,
      department: r.department,
      employmentType: r.employmentType,
      seniority: r.seniority,
      openings: r.openings,
      workArrangement: r.workArrangement,
      location: r.workArrangement === 'Remote' && r.location === r.country ? '' : r.location,
      country: r.country,
      salaryMin: r.salaryMin,
      salaryMax: r.salaryMax,
      salaryCurrency: r.salaryCurrency,
      payPeriod: r.payPeriod,
      salaryVisible: r.salaryVisible,
      description: r.description,
      responsibilities: r.responsibilities ?? '',
      requirements: r.requirements ?? '',
      skills: r.skills,
      applicationUrl: r.applicationUrl ?? '',
      applicationEmail: r.applicationEmail ?? '',
      closingDate: r.closingDate,
    };
  }
}
