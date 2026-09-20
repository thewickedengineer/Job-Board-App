import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { DialogHostComponent } from './shared/dialog-host.component';
import { ToastHostComponent } from './shared/toast-host.component';

@Component({
  imports: [RouterOutlet, ToastHostComponent, DialogHostComponent],
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a class="skip-link" href="#main">Skip to content</a>
    <router-outlet />
    <app-toast-host />
    <app-dialog-host />
  `,
})
export class App {}
