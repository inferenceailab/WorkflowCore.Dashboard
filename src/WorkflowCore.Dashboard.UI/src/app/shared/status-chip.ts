import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { humanize, statusTone } from '../core/format';

@Component({
  selector: 'wfc-status',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="dot"></span>{{ label() }}`,
  host: { '[class]': '"tone-" + tone()' },
  styles: `
    :host {
      --c: var(--wfc-pending);
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 2px 10px 2px 8px;
      border-radius: 999px;
      font: var(--mat-sys-label-medium);
      white-space: nowrap;
      color: var(--c);
      background: color-mix(in srgb, var(--c) 13%, transparent);
    }
    .dot { width: 7px; height: 7px; border-radius: 50%; background: currentColor; }
    :host(.tone-runnable) { --c: var(--wfc-runnable); }
    :host(.tone-suspended) { --c: var(--wfc-suspended); }
    :host(.tone-complete) { --c: var(--wfc-complete); }
    :host(.tone-terminated) { --c: var(--wfc-terminated); }
    :host(.tone-failed) { --c: var(--wfc-failed); }
    :host(.tone-waiting) { --c: var(--wfc-waiting); }
    :host(.tone-sleeping) { --c: var(--wfc-sleeping); }
  `,
})
export class StatusChip {
  readonly status = input.required<string>();
  protected readonly tone = computed(() => statusTone(this.status()));
  protected readonly label = computed(() => humanize(this.status()));
}
