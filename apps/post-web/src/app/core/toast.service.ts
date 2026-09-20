import { Injectable, signal } from '@angular/core';

export interface Toast {
  id: number;
  kind: 'info' | 'success' | 'error';
  message: string;
  action?: { label: string; run: () => void };
}

/** 5 s auto-dismiss for info/success, paused on hover/focus; errors never auto-dismiss (wireframe 3.2). */
@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly toasts = signal<Toast[]>([]);
  private next = 1;

  show(kind: Toast['kind'], message: string, action?: Toast['action']): number {
    const id = this.next++;
    this.toasts.update((list) => [...list, { id, kind, message, action }]);
    return id;
  }

  info(message: string) { return this.show('info', message); }
  success(message: string) { return this.show('success', message); }
  error(message: string, action?: Toast['action']) { return this.show('error', message, action); }

  dismiss(id: number): void {
    this.toasts.update((list) => list.filter((t) => t.id !== id));
  }
}
