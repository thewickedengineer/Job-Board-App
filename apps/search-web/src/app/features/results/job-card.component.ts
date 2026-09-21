import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { closesSoon, closesText, postedAgo, salaryText } from '../../core/format';
import { JobSummary, label } from '../../core/models';

/** A results card (wireframe 2.1): h2 wraps the only link; the whole card is the click target. */
@Component({
  selector: 'app-job-card',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'job-card', '[attr.tabindex]': 'null' },
  template: `
    <article class="card job">
      <div class="row-between job-head">
        <h2 class="job-title"><a [routerLink]="['/jobs', job().slug]" [state]="{ fromResults: true, total: resultsTotal() }" class="stretched">{{ job().title }}</a></h2>
        @if (soon()) { <span class="status status-warn">{{ closes() }}</span> }
      </div>
      <p class="meta">
        {{ job().department }} · {{ job().location }}
        <span class="badge">{{ label(job().workArrangement) }}</span> · {{ label(job().employmentType) }} · {{ job().seniority }}
      </p>
      <p class="salary">{{ salary() }}</p>
      <p class="small muted">{{ job().organization }} · {{ posted() }}</p>
      <p class="excerpt">{{ job().excerpt }}</p>
    </article>
  `,
  styles: `
    :host { display: block; }
    .job { position: relative; display: flex; flex-direction: column; gap: 6px; transition: border-color 120ms; }
    .job:hover { border-color: var(--color-ink); }
    .job:focus-within { outline: 3px solid var(--color-accent); outline-offset: 2px; }
    .job-title { font-size: var(--text-lg); }
    .job-title a { color: var(--color-ink); text-decoration: none; }
    .job-title a:focus-visible { outline: none; }
    .stretched::after { content: ""; position: absolute; inset: 0; }
    .meta { color: var(--color-mid); font-size: var(--text-md); }
    .badge { display: inline-block; padding: 0 6px; border: 1px solid var(--color-line); border-radius: var(--radius-sm); font-size: var(--text-xs); font-weight: 650; color: var(--color-ink); margin: 0 2px; }
    .salary { font-weight: 650; }
    .excerpt { color: #3a3a3a; font-size: var(--text-md); display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; }
    @media (max-width: 720px) { .excerpt { display: none; } }
  `,
})
export class JobCardComponent {
  readonly job = input.required<JobSummary>();
  /** Lets the detail page say "Back to N results". */
  readonly resultsTotal = input<number | null>(null);
  readonly label = label;
  readonly salary = computed(() => salaryText(this.job()));
  readonly posted = computed(() => postedAgo(this.job().publishedAt));
  readonly closes = computed(() => closesText(this.job().closingDate));
  readonly soon = computed(() => closesSoon(this.job().closingDate));
}
