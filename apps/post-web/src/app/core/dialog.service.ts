import { Injectable, signal } from '@angular/core';

export interface DialogRequest {
  title: string;
  body: string;
  confirmLabel: string;
  cancelLabel: string;
  destructive?: boolean;
  /** When set, the confirm button stays disabled until the user types this word. */
  typeToConfirm?: string;
}

interface OpenDialog extends DialogRequest {
  resolve: (confirmed: boolean) => void;
}

/** One modal at a time; the host component renders whatever is `current`. */
@Injectable({ providedIn: 'root' })
export class DialogService {
  readonly current = signal<OpenDialog | null>(null);

  confirm(request: DialogRequest): Promise<boolean> {
    return new Promise((resolve) => {
      this.current.set({
        ...request,
        resolve: (confirmed) => {
          this.current.set(null);
          resolve(confirmed);
        },
      });
    });
  }
}
