import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { JobPostingStatus } from '../core/models';

/** Text carries the meaning; weight and border differentiate, never colour alone. */
@Component({
  selector: 'app-status-chip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="status" [class]="'status status-' + status().toLowerCase()">{{ status() }}</span>`,
})
export class StatusChipComponent {
  readonly status = input.required<JobPostingStatus>();
  readonly label = computed(() => this.status());
}
