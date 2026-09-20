import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

/** The unauthenticated frame shared by login and signup (wireframes 1.1, 1.2). */
@Component({
  selector: 'app-auth-layout',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="auth-header row-between" role="banner">
      <a routerLink="/login" class="brand">Post</a>
      <p class="small muted">
        {{ asideText() }}
        <a [routerLink]="asideLink()">{{ asideLabel() }}</a>
      </p>
    </header>
    <main id="main" class="auth-main" tabindex="-1">
      <div class="auth-card card">
        <ng-content />
      </div>
    </main>
  `,
  styles: `
    .auth-header { max-width: 480px; margin: 0 auto; padding: var(--space-4) var(--gutter); }
    .brand { font-weight: 800; color: var(--color-ink); text-decoration: none; }
    .auth-main { max-width: 480px; margin: var(--space-4) auto var(--space-7); padding: 0 var(--gutter); outline: none; }
    .auth-card { padding: var(--space-5); }
  `,
})
export class AuthLayoutComponent {
  readonly asideText = input.required<string>();
  readonly asideLink = input.required<string>();
  readonly asideLabel = input.required<string>();
}
