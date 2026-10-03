import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, signal } from '@angular/core';
import { MatIcon } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { humanize, shortId, statusTone } from '../core/format';
import { ActivityEntry } from '../core/models';

/** Lifecycle event list used by the overview feed and the instance activity tab. */
@Component({
  selector: 'wfc-activity-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink, MatIcon],
  template: `
    @for (e of entries(); track e.sequence) {
      <div class="entry" [class]="'tone-' + tone(e)">
        <span class="marker"></span>
        <div class="body">
          <div class="line">
            <strong>{{ label(e) }}</strong>
            @if (e.stepName) {
              <span class="step">{{ e.stepName }}</span>
            }
            @if (showInstance()) {
              <a class="instance mono" [routerLink]="['/instances', e.instanceId]">
                {{ e.definitionId }} · {{ short(e.instanceId) }}
              </a>
            }
            <span class="time muted" [title]="e.time | date: 'medium'">{{ e.time | date: 'HH:mm:ss' }}</span>
          </div>
          @if (e.message) {
            <div class="message">{{ e.message }}</div>
          }
          @if (e.details) {
            <button class="details-toggle" (click)="toggle(e.sequence)">
              <mat-icon>{{ expanded().has(e.sequence) ? 'expand_less' : 'expand_more' }}</mat-icon>
              Stack trace
            </button>
            @if (expanded().has(e.sequence)) {
              <pre class="details">{{ e.details }}</pre>
            }
          }
        </div>
      </div>
    } @empty {
      <div class="empty">{{ emptyText() }}</div>
    }
  `,
  styles: `
    :host { display: block; }
    .entry {
      --c: var(--wfc-pending);
      display: flex;
      gap: 12px;
      padding: 10px 16px;
      border-bottom: 1px solid var(--wfc-border);
    }
    .entry:last-child { border-bottom: 0; }
    .tone-runnable { --c: var(--wfc-runnable); }
    .tone-suspended { --c: var(--wfc-suspended); }
    .tone-complete { --c: var(--wfc-complete); }
    .tone-terminated { --c: var(--wfc-terminated); }
    .tone-failed { --c: var(--wfc-failed); }
    .marker { flex: none; width: 8px; height: 8px; margin-top: 6px; border-radius: 50%; background: var(--c); }
    .body { flex: 1; min-width: 0; }
    .line { display: flex; flex-wrap: wrap; align-items: baseline; gap: 4px 10px; }
    strong { font: var(--mat-sys-label-large); }
    .step {
      padding: 0 8px;
      border-radius: 6px;
      background: var(--mat-sys-surface-container-high);
      font: var(--mat-sys-label-medium);
    }
    .instance { text-decoration: none; font-size: 12px; }
    .instance:hover { text-decoration: underline; }
    .time { margin-left: auto; font: var(--mat-sys-label-small); font-variant-numeric: tabular-nums; }
    .message { margin-top: 4px; color: var(--wfc-failed); font: var(--mat-sys-body-small); overflow-wrap: anywhere; }
    .details-toggle {
      display: inline-flex;
      align-items: center;
      gap: 2px;
      margin-top: 4px;
      padding: 0;
      border: 0;
      background: none;
      color: var(--mat-sys-primary);
      font: var(--mat-sys-label-medium);
      cursor: pointer;
      mat-icon { font-size: 18px; width: 18px; height: 18px; }
    }
    .details {
      margin: 6px 0 0;
      padding: 10px 12px;
      max-height: 280px;
      overflow: auto;
      border-radius: 8px;
      background: var(--mat-sys-surface-container);
      font-family: var(--wfc-mono);
      font-size: 11.5px;
      line-height: 1.5;
    }
  `,
})
export class ActivityList {
  readonly entries = input.required<ActivityEntry[]>();
  readonly showInstance = input(true);
  readonly emptyText = input('No activity yet.');

  protected readonly expanded = signal(new Set<number>());

  protected tone(e: ActivityEntry): string {
    return statusTone(e.type);
  }

  protected label(e: ActivityEntry): string {
    return humanize(e.type);
  }

  protected short(id: string): string {
    return shortId(id);
  }

  protected toggle(sequence: number): void {
    const next = new Set(this.expanded());
    if (!next.delete(sequence)) next.add(sequence);
    this.expanded.set(next);
  }
}
