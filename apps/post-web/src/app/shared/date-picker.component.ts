import { ChangeDetectionStrategy, Component, ElementRef, Injector, computed, forwardRef, inject, input, signal, viewChild } from '@angular/core';
import { focusAfterRender } from '../core/focus';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

const DAY_NAMES = ['Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa', 'Su'];
const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

function iso(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function parse(value: string): Date | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  return m ? new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3])) : null;
}

interface Cell {
  date: Date;
  iso: string;
  inMonth: boolean;
  disabled: boolean;
}

/**
 * Wireframe 3.2 "Date picker": text field that accepts yyyy-mm-dd, plus a
 * trigger (aria-haspopup=dialog) that opens a calendar grid. The grid supports
 * arrows, PageUp/PageDown, Home/End; Esc returns focus to the trigger; days
 * before `min` are aria-disabled and skipped by the arrow keys.
 */
@Component({
  selector: 'app-date-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => DatePickerComponent), multi: true }],
  template: `
    <div class="date-picker">
      <div class="input-group">
        <input
          #field
          [id]="inputId()"
          class="input"
          type="text"
          inputmode="numeric"
          placeholder="yyyy-mm-dd"
          autocomplete="off"
          [value]="value()"
          [disabled]="disabled()"
          [attr.aria-invalid]="invalid() ? 'true' : null"
          [attr.aria-describedby]="describedBy()"
          (input)="onInput(field.value)"
          (blur)="onTouched()"
        />
        <button
          #trigger
          type="button"
          class="btn btn-sm input-addon"
          aria-haspopup="dialog"
          [attr.aria-expanded]="open()"
          [disabled]="disabled()"
          (click)="toggle()"
          aria-label="Choose a date"
        >▦</button>
      </div>

      @if (open()) {
        <div #grid class="calendar" role="dialog" aria-modal="false" aria-label="Choose a closing date" (keydown)="onGridKeydown($event)">
          <div class="calendar-head">
            <button type="button" class="btn btn-ghost btn-sm" (click)="shiftMonth(-1)" aria-label="Previous month">←</button>
            <span class="calendar-title" aria-live="polite">{{ monthLabel() }}</span>
            <button type="button" class="btn btn-ghost btn-sm" (click)="shiftMonth(1)" aria-label="Next month">→</button>
          </div>
          <table class="calendar-grid" role="grid">
            <thead>
              <tr>
                @for (d of dayNames; track d) { <th scope="col" abbr="{{ d }}">{{ d }}</th> }
              </tr>
            </thead>
            <tbody>
              @for (week of weeks(); track $index) {
                <tr>
                  @for (cell of week; track cell.iso) {
                    <td>
                      <button
                        type="button"
                        class="day"
                        [class.is-outside]="!cell.inMonth"
                        [class.is-selected]="cell.iso === value()"
                        [attr.tabindex]="cell.iso === focused() ? 0 : -1"
                        [attr.aria-disabled]="cell.disabled ? 'true' : null"
                        [attr.aria-selected]="cell.iso === value() ? 'true' : null"
                        [attr.data-iso]="cell.iso"
                        (click)="pick(cell)"
                      >{{ cell.date.getDate() }}</button>
                    </td>
                  }
                </tr>
              }
            </tbody>
          </table>
          <p class="hint">Past dates are unavailable.</p>
        </div>
      }
    </div>
  `,
  styles: `
    .date-picker { position: relative; }
    .calendar { position: absolute; z-index: 20; top: calc(100% + 4px); left: 0; background: var(--color-surface); border: 1px solid var(--color-line); border-radius: var(--radius-md); box-shadow: var(--shadow-pop); padding: var(--space-3); width: 280px; }
    .calendar-head { display: flex; align-items: center; justify-content: space-between; margin-bottom: var(--space-2); }
    .calendar-title { font-weight: 650; font-size: var(--text-md); }
    .calendar-grid th { font-size: var(--text-xs); color: var(--color-mid); font-weight: 600; padding: 2px; text-align: center; }
    .calendar-grid td { padding: 1px; text-align: center; }
    .day { width: 34px; height: 34px; border: 1px solid transparent; border-radius: var(--radius-sm); background: transparent; cursor: pointer; font-size: var(--text-md); }
    .day:hover { background: var(--color-surface-muted); }
    .day.is-outside { color: var(--color-muted); }
    .day.is-selected { background: var(--color-accent); color: #fff; }
    .day[aria-disabled="true"] { color: var(--color-disabled-ink); text-decoration: line-through; cursor: not-allowed; }
  `,
})
export class DatePickerComponent implements ControlValueAccessor {
  readonly inputId = input.required<string>();
  /** yyyy-mm-dd; days before this are disabled. */
  readonly min = input<string | null>(null);
  readonly describedBy = input<string | null>(null);
  readonly invalid = input(false);

  readonly value = signal('');
  readonly disabled = signal(false);
  readonly open = signal(false);
  readonly focused = signal('');
  readonly viewMonth = signal(new Date());
  readonly dayNames = DAY_NAMES;

  private readonly injector = inject(Injector);
  private readonly trigger = viewChild<ElementRef<HTMLButtonElement>>('trigger');
  private readonly grid = viewChild<ElementRef<HTMLElement>>('grid');

  readonly monthLabel = computed(() => `${MONTHS[this.viewMonth().getMonth()]} ${this.viewMonth().getFullYear()}`);

  readonly weeks = computed<Cell[][]>(() => {
    const first = new Date(this.viewMonth().getFullYear(), this.viewMonth().getMonth(), 1);
    const start = new Date(first);
    start.setDate(first.getDate() - ((first.getDay() + 6) % 7)); // Monday-first
    const min = this.min();
    const weeks: Cell[][] = [];
    for (let w = 0; w < 6; w++) {
      const week: Cell[] = [];
      for (let d = 0; d < 7; d++) {
        const date = new Date(start);
        date.setDate(start.getDate() + w * 7 + d);
        const value = iso(date);
        week.push({ date, iso: value, inMonth: date.getMonth() === first.getMonth(), disabled: min !== null && value < min });
      }
      weeks.push(week);
    }
    return weeks;
  });

  private onChange: (value: string) => void = () => undefined;
  onTouched: () => void = () => undefined;

  writeValue(value: string | null): void {
    this.value.set(value ?? '');
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }

  onInput(raw: string): void {
    this.value.set(raw);
    this.onChange(raw);
  }

  toggle(): void {
    if (this.open()) {
      this.close();
      return;
    }
    const current = parse(this.value()) ?? parse(this.min() ?? '') ?? new Date();
    this.viewMonth.set(new Date(current.getFullYear(), current.getMonth(), 1));
    this.focused.set(iso(current));
    this.open.set(true);
    this.focusCell(this.focused());
  }

  close(): void {
    this.open.set(false);
    this.trigger()?.nativeElement.focus();
  }

  shiftMonth(delta: number): void {
    const m = this.viewMonth();
    this.viewMonth.set(new Date(m.getFullYear(), m.getMonth() + delta, 1));
  }

  pick(cell: Cell): void {
    if (cell.disabled) {
      return;
    }
    this.value.set(cell.iso);
    this.onChange(cell.iso);
    this.onTouched();
    this.close();
  }

  onGridKeydown(event: KeyboardEvent): void {
    const current = parse(this.focused());
    if (!current) {
      return;
    }
    const move = (days: number, months = 0) => {
      event.preventDefault();
      let next = new Date(current.getFullYear(), current.getMonth() + months, current.getDate() + days);
      const min = this.min();
      // Disabled days are skipped, never landed on.
      if (min && iso(next) < min) {
        next = parse(min)!;
      }
      this.viewMonth.set(new Date(next.getFullYear(), next.getMonth(), 1));
      this.focused.set(iso(next));
      this.focusCell(iso(next));
    };

    switch (event.key) {
      case 'ArrowLeft': return move(-1);
      case 'ArrowRight': return move(1);
      case 'ArrowUp': return move(-7);
      case 'ArrowDown': return move(7);
      case 'PageUp': return move(0, -1);
      case 'PageDown': return move(0, 1);
      case 'Home': return move(-((current.getDay() + 6) % 7));
      case 'End': return move(6 - ((current.getDay() + 6) % 7));
      case 'Escape':
        event.preventDefault();
        return this.close();
      case 'Enter':
      case ' ': {
        event.preventDefault();
        const cell = this.weeks().flat().find((c) => c.iso === this.focused());
        if (cell) {
          this.pick(cell);
        }
        return;
      }
      default:
        return;
    }
  }

  private focusCell(value: string): void {
    // The grid (or the new month) renders on the next change detection.
    focusAfterRender(this.injector, () => this.grid()?.nativeElement.querySelector<HTMLElement>(`[data-iso="${value}"]`));
  }
}
