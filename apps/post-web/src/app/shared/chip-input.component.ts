import { ChangeDetectionStrategy, Component, forwardRef, input, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

/**
 * Wireframe 3.2 "Chip input": commit on Enter or comma, Backspace on an empty
 * field removes the last chip, duplicates and blanks are dropped silently.
 * A ControlValueAccessor so the reactive form sees one `skills` control.
 */
@Component({
  selector: 'app-chip-input',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => ChipInputComponent), multi: true }],
  template: `
    <div class="chip-input" [class.is-disabled]="disabled()" (click)="field.focus()">
      @for (chip of chips(); track chip; let i = $index) {
        <span class="chip">
          {{ chip }}
          <button type="button" class="chip-remove" [disabled]="disabled()" (click)="remove(i); $event.stopPropagation()" [attr.aria-label]="'Remove ' + chip">✕</button>
        </span>
      }
      <input
        #field
        [id]="inputId()"
        class="chip-field"
        [placeholder]="chips().length === 0 ? placeholder() : ''"
        [disabled]="disabled()"
        [attr.aria-describedby]="describedBy()"
        [attr.aria-invalid]="invalid() ? 'true' : null"
        autocomplete="off"
        (keydown)="onKeydown($event, field)"
        (blur)="commit(field); onTouched()"
      />
    </div>
  `,
  styles: `
    .chip-input { display: flex; flex-wrap: wrap; gap: 6px; align-items: center; min-height: var(--control-height); padding: 4px 8px; border: 1px solid #b0b0b0; border-radius: var(--radius-sm); background: var(--color-surface); cursor: text; }
    .chip-input:focus-within { border-color: var(--color-accent); box-shadow: var(--focus-ring); }
    .chip-input.is-disabled { background: var(--color-disabled-bg); border-style: dashed; cursor: not-allowed; }
    .chip-field { flex: 1 1 120px; min-width: 120px; border: 0; outline: none; background: transparent; padding: 4px 2px; font-size: var(--text-md); }
  `,
})
export class ChipInputComponent implements ControlValueAccessor {
  readonly inputId = input.required<string>();
  readonly placeholder = input('Type and press Enter…');
  readonly max = input(20);
  readonly describedBy = input<string | null>(null);
  readonly invalid = input(false);

  readonly chips = signal<string[]>([]);
  readonly disabled = signal(false);

  private onChange: (value: string[]) => void = () => undefined;
  onTouched: () => void = () => undefined;

  writeValue(value: string[] | null): void {
    this.chips.set(value ?? []);
  }

  registerOnChange(fn: (value: string[]) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }

  onKeydown(event: KeyboardEvent, field: HTMLInputElement): void {
    if (event.key === 'Enter' || event.key === ',') {
      event.preventDefault();
      this.commit(field);
    } else if (event.key === 'Backspace' && field.value === '' && this.chips().length > 0) {
      this.remove(this.chips().length - 1);
    }
  }

  commit(field: HTMLInputElement): void {
    const value = field.value.trim();
    field.value = '';
    if (!value) {
      return;
    }
    const exists = this.chips().some((c) => c.localeCompare(value, undefined, { sensitivity: 'accent' }) === 0);
    if (!exists) {
      this.update([...this.chips(), value]);
    }
  }

  remove(index: number): void {
    this.update(this.chips().filter((_, i) => i !== index));
  }

  private update(chips: string[]): void {
    this.chips.set(chips);
    this.onChange(chips);
  }
}
