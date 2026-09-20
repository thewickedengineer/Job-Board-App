import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../core/auth.service';

/**
 * Wireframe 3.1, App 1: persistent left-aligned nav, signed-in identity, app
 * switcher to the public board, content max-width 1080px, thin legal line.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="shell-header" role="banner">
      <div class="shell-inner row-between">
        <nav aria-label="Primary" class="row">
          <a routerLink="/" class="brand">Post</a>
          <a routerLink="/" routerLinkActive="is-active" [routerLinkActiveOptions]="{ exact: true }" ariaCurrentWhenActive="page" class="nav-link">Postings</a>
          <a [href]="jobBoardUrl" target="_blank" rel="noopener" class="nav-link switcher">⇄ Job board ↗</a>
        </nav>
        <div class="row identity">
          <span class="muted small">{{ organization() }}</span>
          <span class="who">{{ shortName() }}</span>
          <button type="button" class="btn btn-ghost btn-sm" (click)="signOut()">Sign out</button>
        </div>
      </div>
    </header>
    <main id="main" class="shell-main" tabindex="-1">
      <router-outlet />
    </main>
    <footer class="shell-footer" role="contentinfo">
      <div class="shell-inner muted small">© TalentBridge · Internal hiring portal · Privacy</div>
    </footer>
  `,
  styles: `
    .shell-header { background: var(--color-surface); border-bottom: 1px solid var(--color-line-soft); }
    .shell-inner { max-width: var(--content-max); margin: 0 auto; padding: 0 var(--gutter); min-height: 52px; }
    .brand { font-weight: 800; letter-spacing: 0.02em; color: var(--color-ink); text-decoration: none; margin-right: var(--space-3); }
    .nav-link { color: var(--color-ink); text-decoration: none; padding: 14px 8px; border-bottom: 2px solid transparent; font-size: var(--text-md); font-weight: 600; }
    .nav-link.is-active { border-bottom-color: var(--color-accent); }
    .switcher { color: var(--color-mid); font-weight: 500; }
    .identity { gap: var(--space-3); }
    .who { font-weight: 650; font-size: var(--text-md); }
    .shell-main { max-width: var(--content-max); margin: 0 auto; padding: var(--space-5) var(--gutter) var(--space-7); outline: none; }
    .shell-footer { border-top: 1px solid var(--color-line-soft); padding: var(--space-3) 0; }
  `,
})
export class ShellComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly jobBoardUrl = environment.jobBoardUrl;
  readonly organization = computed(() => this.auth.manager()?.organization ?? '');
  readonly shortName = computed(() => {
    const name = this.auth.manager()?.fullName ?? '';
    const [first, ...rest] = name.split(' ');
    return rest.length ? `${first} ${rest[rest.length - 1][0]}.` : first;
  });

  async signOut(): Promise<void> {
    await this.auth.logout();
    await this.router.navigate(['/login']);
  }
}
