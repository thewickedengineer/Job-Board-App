import { ChangeDetectionStrategy, Component, ElementRef, afterRenderEffect, computed, inject, signal, viewChild } from '@angular/core';
import { DialogService } from '../core/dialog.service';

/**
 * role=alertdialog, focus trapped, initial focus on the first input, Esc
 * cancels, focus returns to the opener when it closes (wireframe 1.6).
 */
@Component({
  selector: 'app-dialog-host',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (dialogs.current(); as dialog) {
      <div class="backdrop" (click)="dialog.resolve(false)"></div>
      <div
        #panel
        class="dialog"
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="dialog-title"
        aria-describedby="dialog-body"
        tabindex="-1"
        (keydown)="onKeydown($event)"
      >
        <h2 id="dialog-title">{{ dialog.title }}</h2>
        <p id="dialog-body">{{ dialog.body }}</p>
        @if (dialog.typeToConfirm; as word) {
          <div class="field">
            <label class="label" for="dialog-confirm-input">Type {{ word }} to confirm</label>
            <input #confirmInput id="dialog-confirm-input" class="input" autocomplete="off" [value]="typed()" (input)="typed.set(confirmInput.value)" />
          </div>
        }
        <div class="row dialog-actions">
          <button type="button" class="btn" (click)="dialog.resolve(false)">{{ dialog.cancelLabel }}</button>
          <button
            type="button"
            class="btn"
            [class.btn-danger]="dialog.destructive"
            [class.btn-primary]="!dialog.destructive"
            [disabled]="!canConfirm()"
            aria-describedby="dialog-body"
            (click)="dialog.resolve(true)"
          >
            {{ dialog.confirmLabel }}
          </button>
        </div>
      </div>
    }
  `,
  styles: `
    .backdrop { position: fixed; inset: 0; background: rgba(28, 28, 28, 0.45); z-index: 50; }
    .dialog { position: fixed; z-index: 51; left: 50%; top: 50%; transform: translate(-50%, -50%); width: min(460px, calc(100vw - 32px)); background: var(--color-surface); border-radius: var(--radius-md); box-shadow: var(--shadow-pop); padding: var(--space-5); display: flex; flex-direction: column; gap: var(--space-3); }
    .dialog-actions { justify-content: flex-end; margin-top: var(--space-2); }
  `,
})
export class DialogHostComponent {
  readonly dialogs = inject(DialogService);
  readonly typed = signal('');
  readonly canConfirm = computed(() => {
    const dialog = this.dialogs.current();
    return !dialog?.typeToConfirm || this.typed().trim() === dialog.typeToConfirm;
  });

  private readonly panel = viewChild<ElementRef<HTMLElement>>('panel');
  private opener: HTMLElement | null = null;

  constructor() {
    afterRenderEffect(() => {
      const dialog = this.dialogs.current();
      const panel = this.panel()?.nativeElement;
      if (dialog && panel) {
        this.opener ??= document.activeElement as HTMLElement;
        this.typed.set('');
        (panel.querySelector<HTMLElement>('input') ?? panel).focus();
      } else if (!dialog && this.opener) {
        this.opener.focus();
        this.opener = null;
      }
    });
  }

  onKeydown(event: KeyboardEvent): void {
    const dialog = this.dialogs.current();
    if (!dialog) {
      return;
    }
    if (event.key === 'Escape') {
      event.preventDefault();
      dialog.resolve(false);
    } else if (event.key === 'Tab') {
      // Trap focus inside the panel.
      const panel = this.panel()!.nativeElement;
      const focusable = Array.from(panel.querySelectorAll<HTMLElement>('input, button:not(:disabled), [tabindex="0"]'));
      if (focusable.length === 0) {
        return;
      }
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    }
  }
}
