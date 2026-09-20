import { ChangeDetectionStrategy, Component, ElementRef, input, output, viewChild } from '@angular/core';

export interface SummaryEntry {
  /** Id of the element to focus when the entry is activated; null renders plain text. */
  targetId: string | null;
  label: string;
  message: string;
}

/**
 * Wireframe 3.2 "Error summary banner": role=alert, focused on arrival, an
 * ordered list so count and position are announced, each line a link that
 * focuses its input.
 */
@Component({
  selector: 'app-error-summary',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (entries().length > 0) {
      <div #banner class="banner banner-error" role="alert" tabindex="-1" [attr.aria-labelledby]="headingId">
        <p class="banner-title" [id]="headingId">{{ title() }}</p>
        <ol>
          @for (entry of entries(); track entry.label + entry.message) {
            <li>
              @if (entry.targetId) {
                <a href="#{{ entry.targetId }}" (click)="focusTarget($event, entry.targetId)">{{ entry.label }}</a>
                — {{ entry.message }}
              } @else {
                <strong>{{ entry.label }}</strong> — {{ entry.message }}
              }
            </li>
          }
        </ol>
      </div>
    }
  `,
})
export class ErrorSummaryComponent {
  readonly entries = input.required<SummaryEntry[]>();
  readonly title = input('Problems need attention');
  readonly navigated = output<string>();

  private readonly banner = viewChild<ElementRef<HTMLElement>>('banner');
  readonly headingId = `error-summary-${Math.random().toString(36).slice(2, 8)}`;

  focus(): void {
    this.banner()?.nativeElement.focus();
  }

  element(): HTMLElement | null {
    return this.banner()?.nativeElement ?? null;
  }

  focusTarget(event: Event, id: string): void {
    event.preventDefault();
    const target = document.getElementById(id);
    if (target) {
      // Smooth scroll with room for the sticky header, then focus without a second jump.
      target.scrollIntoView?.({ behavior: 'smooth', block: 'center' });
      target.focus({ preventScroll: true });
      this.navigated.emit(id);
    }
  }
}

/** Convenience for focusAfterRender callers. */
export function bannerOf(summary: ErrorSummaryComponent): HTMLElement | null {
  return summary.element();
}
