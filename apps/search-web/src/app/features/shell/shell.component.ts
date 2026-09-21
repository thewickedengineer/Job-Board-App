import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { map } from 'rxjs';

/**
 * Wireframe 3.1, App 2: wide, anonymous, search in the header because it is
 * the primary action, a real footer, no auth chrome at all — the shell renders
 * identically for every visitor.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="site-header" role="banner">
      <div class="site-inner row-between">
        <a routerLink="/" class="brand">TalentBridge <span class="brand-sub">Careers</span></a>
        <form class="header-search" role="search" (submit)="search($event, box.value)">
          <label class="visually-hidden" for="header-q">Search roles</label>
          <input #box id="header-q" class="input" type="search" placeholder="Search roles ⌕" autocomplete="off" [value]="q()" (input)="typed.next(box.value)" />
          <button type="submit" class="btn btn-primary">Search</button>
        </form>
        <nav aria-label="Primary" class="row site-nav">
          <a routerLink="/" routerLinkActive="is-active" [routerLinkActiveOptions]="{ exact: true }" ariaCurrentWhenActive="page">All roles</a>
        </nav>
      </div>
    </header>
    <main id="main" class="site-main" tabindex="-1">
      <router-outlet />
    </main>
    <footer class="site-footer" role="contentinfo">
      <div class="site-inner row-between">
        <p class="small muted">© TalentBridge · Every listing is posted directly by the hiring organisation.</p>
        <nav aria-label="Footer" class="row small">
          <a href="#about" (click)="$event.preventDefault()">About</a>
          <a href="#equal-opportunity" (click)="$event.preventDefault()">Equal-opportunity statement</a>
          <a href="#privacy" (click)="$event.preventDefault()">Privacy</a>
          <a href="#sitemap" (click)="$event.preventDefault()">Sitemap</a>
        </nav>
      </div>
    </footer>
  `,
  styles: `
    .site-header { background: var(--color-surface); border-bottom: 1px solid var(--color-line-soft); }
    .site-inner { max-width: var(--content-max); margin: 0 auto; padding: var(--space-3) var(--gutter); }
    .brand { font-weight: 800; font-size: var(--text-lg); color: var(--color-ink); text-decoration: none; white-space: nowrap; }
    .brand-sub { font-weight: 500; color: var(--color-mid); }
    .header-search { display: flex; gap: var(--space-2); flex: 1 1 320px; max-width: 560px; }
    .site-nav a { color: var(--color-ink); text-decoration: none; font-weight: 600; padding: 8px 4px; border-bottom: 2px solid transparent; }
    .site-nav a.is-active { border-bottom-color: var(--color-accent); }
    .site-main { max-width: var(--content-max); margin: 0 auto; padding: var(--space-5) var(--gutter) var(--space-7); outline: none; }
    .site-footer { border-top: 1px solid var(--color-line-soft); margin-top: var(--space-6); }
    .site-footer nav { gap: var(--space-4); }
    @media (max-width: 720px) {
      .site-inner { gap: var(--space-2); }
      .header-search { flex-basis: 100%; order: 3; }
      .site-nav { display: none; }
      .site-main { padding: var(--space-4) var(--gutter) 96px; }
    }
  `,
})
export class ShellComponent {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  readonly q = toSignal(this.route.queryParamMap.pipe(map((p) => p.get('q') ?? '')), { initialValue: '' });

  /** Keystrokes are debounced 300 ms (CLAUDE.md §10); Enter submits immediately. */
  readonly typed = new Subject<string>();

  constructor() {
    this.typed.pipe(debounceTime(300), distinctUntilChanged()).subscribe((value) => this.go(value));
  }

  /** Header search submits to the results page with the keyword param (wireframe 3.1). */
  search(event: Event, value: string): void {
    event.preventDefault();
    this.go(value);
  }

  private go(value: string): void {
    void this.router.navigate(['/'], { queryParams: { q: value.trim() || null, page: null }, queryParamsHandling: 'merge' });
  }
}
