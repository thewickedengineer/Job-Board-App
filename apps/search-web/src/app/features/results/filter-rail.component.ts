import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { FacetValue, Facets, JobFilters, POSTED_WITHIN, SALARY_SLIDER_MAX, SENIORITIES, label } from '../../core/models';

const VISIBLE_DEPARTMENTS = 4;
const SALARY_STEP = 1000;

/**
 * The filter rail (wireframe 2.1): a nav of fieldsets. Emits a patch of the
 * filter state; the parent owns the URL. Rendered in the desktop rail and
 * again inside the mobile bottom sheet.
 */
@Component({
  selector: 'app-filter-rail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav aria-label="Filters" class="rail">
      <div class="row-between rail-head">
        @if (showHeading()) { <h2>Filters</h2> } @else { <span></span> }
        @if (activeCount() > 0) {
          <button type="button" class="btn btn-ghost btn-sm" (click)="changed.emit(cleared())">Clear all</button>
        }
      </div>

      <fieldset class="group">
        <legend class="label">Department</legend>
        @for (f of departments(); track f.value) {
          <label class="check">
            <input type="checkbox" [checked]="filters().department.includes(f.value)" (change)="toggle('department', f.value)" />
            <span>{{ f.value }}</span> <span class="count">({{ f.count }})</span>
          </label>
        }
        @if (hiddenDepartments() > 0) {
          <button type="button" class="btn btn-ghost btn-sm more" (click)="showAllDepartments.set(true)" [attr.aria-expanded]="false">Show {{ hiddenDepartments() }} more</button>
        }
      </fieldset>

      <fieldset class="group">
        <legend class="label"><label for="f-location">Location</label></legend>
        <input #loc id="f-location" class="input" type="search" placeholder="City or region" autocomplete="off" [value]="filters().location"
               (change)="changed.emit({ location: loc.value.trim(), page: 1 })" />
      </fieldset>

      <fieldset class="group">
        <legend class="label">Work arrangement</legend>
        @for (f of facets()?.workArrangements ?? []; track f.value) {
          <label class="check">
            <input type="checkbox" [checked]="filters().workArrangement.includes(f.value)" (change)="toggle('workArrangement', f.value)" />
            <span>{{ label(f.value) }}</span> <span class="count">({{ f.count }})</span>
          </label>
        }
      </fieldset>

      <fieldset class="group">
        <legend class="label">Employment type</legend>
        @for (f of facets()?.employmentTypes ?? []; track f.value) {
          <label class="check">
            <input type="checkbox" [checked]="filters().employmentType.includes(f.value)" (change)="toggle('employmentType', f.value)" />
            <span>{{ label(f.value) }}</span> <span class="count">({{ f.count }})</span>
          </label>
        }
      </fieldset>

      <fieldset class="group">
        <legend class="label"><label for="f-seniority">Seniority</label></legend>
        <select #sen id="f-seniority" class="select" (change)="changed.emit({ seniority: sen.value ? [sen.value] : [], page: 1 })">
          <option value="" [selected]="filters().seniority.length === 0">Any</option>
          @for (s of seniorities; track s) {
            <option [value]="s" [selected]="filters().seniority.includes(s)">{{ s }}</option>
          }
        </select>
      </fieldset>

      <fieldset class="group" aria-describedby="f-salary-text">
        <legend class="label">Salary range</legend>
        <p id="f-salary-text" class="small salary-text" aria-live="polite">{{ salaryText() }}</p>
        <label class="visually-hidden" for="f-salary-min">Minimum salary</label>
        <input id="f-salary-min" type="range" min="0" [max]="sliderMax" [step]="step" [value]="filters().salaryMin ?? 0"
               (input)="previewSalary(+salMin.value, null)" (change)="applySalary(+salMin.value, null)" #salMin />
        <label class="visually-hidden" for="f-salary-max">Maximum salary</label>
        <input id="f-salary-max" type="range" min="0" [max]="sliderMax" [step]="step" [value]="filters().salaryMax ?? sliderMax"
               (input)="previewSalary(null, +salMax.value)" (change)="applySalary(null, +salMax.value)" #salMax />
      </fieldset>

      <fieldset class="group">
        <legend class="label">Posted within</legend>
        @for (opt of postedWithin; track opt.label) {
          <label class="check">
            <input type="radio" name="postedWithin" [checked]="filters().postedWithinDays === opt.value" (change)="changed.emit({ postedWithinDays: opt.value, page: 1 })" />
            <span>{{ opt.label }}</span>
          </label>
        }
      </fieldset>
    </nav>
  `,
  styles: `
    .rail { display: flex; flex-direction: column; gap: var(--space-4); }
    .rail-head h2 { font-size: var(--text-base); }
    .group { display: flex; flex-direction: column; gap: 6px; }
    .group legend { margin-bottom: 6px; }
    .check { display: flex; align-items: center; gap: 8px; min-height: 32px; cursor: pointer; font-size: var(--text-md); }
    .check input { width: 18px; height: 18px; margin: 0; }
    .count { color: var(--color-mid); }
    .more { align-self: flex-start; }
    .salary-text { margin: 0 0 4px; font-weight: 600; }
    input[type="range"] { width: 100%; }
  `,
})
export class FilterRailComponent {
  readonly filters = input.required<JobFilters>();
  readonly facets = input<Facets | undefined>(undefined);
  /** False inside the mobile sheet, which has its own heading. */
  readonly showHeading = input(true);
  readonly changed = output<Partial<JobFilters>>();

  readonly seniorities = SENIORITIES;
  readonly postedWithin = POSTED_WITHIN;
  readonly sliderMax = SALARY_SLIDER_MAX;
  readonly step = SALARY_STEP;
  readonly label = label;

  readonly showAllDepartments = signal(false);
  private readonly preview = signal<{ min: number | null; max: number | null } | null>(null);

  /** Facet rows plus any selected value the facets no longer list (so it can be unchecked). */
  readonly allDepartments = computed<FacetValue[]>(() => {
    const listed = this.facets()?.departments ?? [];
    const extra = this.filters().department.filter((d) => !listed.some((f) => f.value === d)).map((value) => ({ value, count: 0 }));
    return [...listed, ...extra];
  });
  readonly departments = computed(() => (this.showAllDepartments() ? this.allDepartments() : this.allDepartments().slice(0, VISIBLE_DEPARTMENTS)));
  readonly hiddenDepartments = computed(() => Math.max(0, this.allDepartments().length - this.departments().length));

  readonly activeCount = computed(() => countActive(this.filters()));

  readonly salaryText = computed(() => {
    const f = this.filters();
    const p = this.preview();
    const min = p?.min ?? f.salaryMin;
    const max = p?.max ?? f.salaryMax;
    const fmt = (n: number) => `${Math.round(n / 1000)}k`;
    if (min === null && max === null) return 'Any salary';
    if (max === null) return `${fmt(min!)}+`;
    if (min === null) return `up to ${fmt(max)}`;
    return `${fmt(min)} – ${fmt(max)}`;
  });

  toggle(key: 'department' | 'workArrangement' | 'employmentType', value: string): void {
    const current = this.filters()[key];
    const next = current.includes(value) ? current.filter((v) => v !== value) : [...current, value];
    this.changed.emit({ [key]: next, page: 1 });
  }

  previewSalary(min: number | null, max: number | null): void {
    this.preview.set({ min: min ?? this.preview()?.min ?? null, max: max ?? this.preview()?.max ?? null });
  }

  applySalary(min: number | null, max: number | null): void {
    this.preview.set(null);
    const f = this.filters();
    const nextMin = min ?? f.salaryMin;
    const nextMax = max ?? f.salaryMax;
    this.changed.emit({
      salaryMin: nextMin && nextMin > 0 ? nextMin : null,
      salaryMax: nextMax !== null && nextMax < SALARY_SLIDER_MAX ? nextMax : null,
      page: 1,
    });
  }

  cleared(): Partial<JobFilters> {
    return { department: [], location: '', workArrangement: [], employmentType: [], seniority: [], salaryMin: null, salaryMax: null, postedWithinDays: null, page: 1 };
  }
}

export function countActive(f: JobFilters): number {
  return (
    f.department.length + f.workArrangement.length + f.employmentType.length + f.seniority.length +
    (f.location.trim() ? 1 : 0) + (f.salaryMin !== null || f.salaryMax !== null ? 1 : 0) + (f.postedWithinDays !== null ? 1 : 0)
  );
}
