import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { Toast, ToastService } from '../core/toast.service';

@Component({
  selector: 'app-toast-host',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="toast-host" aria-live="polite">
      @for (toast of toasts.toasts(); track toast.id) {
        <div
          class="toast"
          [class.toast-error]="toast.kind === 'error'"
          [class.toast-success]="toast.kind === 'success'"
          [attr.role]="toast.kind === 'error' ? 'alert' : 'status'"
          (mouseenter)="pause(toast)"
          (mouseleave)="resume(toast)"
          (focusin)="pause(toast)"
          (focusout)="resume(toast)"
        >
          <span>{{ toast.message }}</span>
          @if (toast.action; as action) {
            <button type="button" class="btn btn-sm" (click)="action.run(); toasts.dismiss(toast.id)">{{ action.label }}</button>
          }
          <button type="button" class="toast-close" (click)="toasts.dismiss(toast.id)" aria-label="Dismiss">✕</button>
        </div>
      }
    </div>
  `,
  styles: `
    .toast-host { position: fixed; right: 16px; bottom: 16px; display: flex; flex-direction: column; gap: 8px; z-index: 60; max-width: min(420px, calc(100vw - 32px)); }
    .toast { display: flex; align-items: center; gap: 12px; padding: 10px 12px; background: var(--color-ink); color: #fff; border-radius: var(--radius-md); box-shadow: var(--shadow-pop); font-size: var(--text-md); }
    .toast-error { background: var(--color-danger); }
    .toast-success { background: var(--color-ok); }
    .toast-close { margin-left: auto; border: 0; background: transparent; color: inherit; cursor: pointer; padding: 4px; border-radius: var(--radius-sm); }
    .toast .btn { color: var(--color-ink); }
  `,
})
export class ToastHostComponent {
  readonly toasts = inject(ToastService);
  private readonly timers = new Map<number, ReturnType<typeof setTimeout>>();

  constructor() {
    // Arm a 5 s timer for each new non-error toast. The effect only schedules
    // work; it never writes signals.
    const seen = new Set<number>();
    effect(() => {
      for (const toast of this.toasts.toasts()) {
        if (!seen.has(toast.id)) {
          seen.add(toast.id);
          if (toast.kind !== 'error') {
            this.arm(toast);
          }
        }
      }
    });
  }

  pause(toast: Toast): void {
    const timer = this.timers.get(toast.id);
    if (timer) {
      clearTimeout(timer);
      this.timers.delete(toast.id);
    }
  }

  resume(toast: Toast): void {
    if (toast.kind !== 'error' && !this.timers.has(toast.id)) {
      this.arm(toast);
    }
  }

  private arm(toast: Toast): void {
    this.timers.set(toast.id, setTimeout(() => {
      this.timers.delete(toast.id);
      this.toasts.dismiss(toast.id);
    }, 5000));
  }
}
