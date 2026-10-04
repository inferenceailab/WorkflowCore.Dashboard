import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { MatTooltip } from '@angular/material/tooltip';
import { durationBetween } from '../core/format';
import { DefinitionDetail, PointerDto } from '../core/models';
import { StatusChip } from './status-chip';
import { stepIcon } from './workflow-graph/graph-model';

/** Details of the step selected in the graph, plus its executions when viewing an instance. */
@Component({
  selector: 'wfc-step-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, MatButton, MatIconButton, MatIcon, MatTooltip, StatusChip],
  template: `
    @if (step(); as s) {
      <div class="head">
        <mat-icon class="icon">{{ icon() }}</mat-icon>
        <div class="title">
          <strong>{{ s.name }}</strong>
          <span class="muted mono">#{{ s.id }} · {{ s.stepType }}</span>
        </div>
        <button mat-icon-button (click)="closed.emit()" matTooltip="Close" aria-label="Close step details">
          <mat-icon>close</mat-icon>
        </button>
      </div>

      @if (pointers(); as runs) {
        <h3>Executions <span class="muted">{{ runs.length }}</span></h3>
        @for (p of runs; track p.id) {
          <div class="run">
            <div class="run-line">
              <wfc-status [status]="displayStatus(p)" />
              <span class="muted">{{ p.startTime ? (p.startTime | date: 'HH:mm:ss.SSS') : 'not started' }}</span>
              <span class="duration">{{ p.startTime ? duration(p) : '' }}</span>
            </div>
            @if (p.retryCount) {
              <div class="muted small">Retried {{ p.retryCount }} {{ p.retryCount === 1 ? 'time' : 'times' }}</div>
            }
            @if (p.eventName) {
              <div class="small">
                Event <span class="mono">{{ p.eventName }}</span> / <span class="mono">{{ p.eventKey || '(empty key)' }}</span>
              </div>
            }
            @if (p.status === 'WaitingForEvent' && allowActions()) {
              <button mat-stroked-button class="publish" (click)="publish.emit(p)">
                <mat-icon>send</mat-icon> Publish event
              </button>
            }
          </div>
        } @empty {
          <p class="muted small">This step has not run in this instance.</p>
        }
      }

      <h3>Definition</h3>
      <dl class="facts">
        <dt>Type</dt>
        <dd class="mono">{{ s.stepTypeFullName }}</dd>
        <dt>Next</dt>
        <dd>{{ nextSteps() || 'End' }}</dd>
        @if (s.children.length) {
          <dt>Branches</dt>
          <dd>{{ childSteps() }}</dd>
        }
        <dt>On error</dt>
        <dd>{{ s.errorBehavior ?? 'Default' }}{{ s.retryInterval ? ' · every ' + s.retryInterval : '' }}</dd>
        @if (s.compensationStepId !== null) {
          <dt>Compensate</dt>
          <dd>{{ name(s.compensationStepId) }}</dd>
        }
        @if (s.externalId) {
          <dt>DSL ID</dt>
          <dd class="mono">{{ s.externalId }}</dd>
        }
      </dl>
    }
  `,
  styles: `
    :host { display: block; }
    .head { display: flex; align-items: center; gap: 10px; padding: 10px 8px 10px 16px; border-bottom: 1px solid var(--wfc-border); }
    .icon { color: var(--mat-sys-tertiary); }
    .title { flex: 1; min-width: 0; display: flex; flex-direction: column; }
    .title strong { font: var(--mat-sys-title-small); overflow-wrap: anywhere; }
    .title span { font-size: 11.5px; overflow-wrap: anywhere; }
    h3 { margin: 14px 16px 6px; font: var(--mat-sys-label-large); }
    .run { padding: 8px 16px; border-bottom: 1px solid var(--wfc-border); }
    .run:last-of-type { border-bottom: 0; }
    .run-line { display: flex; align-items: center; gap: 8px; }
    .duration { margin-left: auto; font-variant-numeric: tabular-nums; font: var(--mat-sys-label-medium); }
    .small { font: var(--mat-sys-body-small); margin-top: 4px; }
    p.small { margin: 0 16px 8px; }
    .publish { margin-top: 8px; }
    dl.facts { padding-top: 4px; }
  `,
})
export class StepPanel {
  readonly definition = input.required<DefinitionDetail>();
  readonly stepId = input.required<number>();
  /** Executions of this step; null when showing a definition only. */
  readonly pointers = input<PointerDto[] | null>(null);
  readonly allowActions = input(false);
  readonly closed = output<void>();
  readonly publish = output<PointerDto>();

  protected readonly step = computed(() => this.definition().steps.find((s) => s.id === this.stepId()) ?? null);
  protected readonly icon = computed(() => stepIcon(this.step()?.stepType ?? ''));
  protected readonly nextSteps = computed(() =>
    (this.step()?.outcomes ?? []).map((o) => this.name(o.nextStep) + (o.label ? ` (${o.label})` : '')).join(', '),
  );
  protected readonly childSteps = computed(() => (this.step()?.children ?? []).map((id) => this.name(id)).join(', '));

  protected name(id: number): string {
    return this.definition().steps.find((s) => s.id === id)?.name ?? `#${id}`;
  }

  protected displayStatus(p: PointerDto): string {
    return p.status === 'Failed' && p.active && p.sleepUntil ? 'Retrying' : p.status;
  }

  protected duration(p: PointerDto): string {
    return durationBetween(p.startTime, p.endTime);
  }
}
