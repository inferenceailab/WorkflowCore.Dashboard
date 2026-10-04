import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTooltip } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { filter } from 'rxjs';
import { upsertActivity } from '../../core/activity';
import { ApiService, errorMessage } from '../../core/api.service';
import { durationBetween, formatDuration, relativeTime, statusTone } from '../../core/format';
import { LiveService } from '../../core/live.service';
import { ActivityEntry, DefinitionDetail, InstanceDetail, PointerDto } from '../../core/models';
import { PublishEventDialog } from '../../dialogs/publish-event-dialog';
import { ActivityList } from '../../shared/activity-list';
import { confirm } from '../../shared/confirm-dialog';
import { JsonView } from '../../shared/json-view';
import { StatusChip } from '../../shared/status-chip';
import { StepPanel } from '../../shared/step-panel';
import { WorkflowGraph } from '../../shared/workflow-graph/workflow-graph';

interface TimelineBar {
  left: number;
  width: number;
  tone: string;
}

@Component({
  selector: 'wfc-instance-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    RouterLink,
    MatButton,
    MatIconButton,
    MatIcon,
    MatProgressBar,
    MatTabsModule,
    MatTooltip,
    StatusChip,
    JsonView,
    ActivityList,
    StepPanel,
    WorkflowGraph,
  ],
  templateUrl: './instance-detail.html',
  styleUrl: './instance-detail.scss',
})
export class InstanceDetailPage {
  private readonly api = inject(ApiService);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);

  readonly id = input.required<string>();

  protected readonly allowActions = computed(() => this.api.config().allowActions);
  protected readonly detail = signal<InstanceDetail | null>(null);
  protected readonly activity = signal<ActivityEntry[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly expanded = signal<string | null>(null);
  protected readonly now = signal(Date.now());
  protected readonly definition = signal<DefinitionDetail | null>(null);
  protected readonly selectedStep = signal<number | null>(null);
  protected readonly journal = computed(() => this.api.config().journal);

  protected readonly summary = computed(() => this.detail()?.summary ?? null);
  protected readonly pointers = computed(() => this.detail()?.executionPointers ?? []);
  protected readonly canSuspend = computed(() => this.summary()?.status === 'Runnable');
  protected readonly canResume = computed(() => this.summary()?.status === 'Suspended');
  protected readonly canTerminate = computed(() => ['Runnable', 'Suspended'].includes(this.summary()?.status ?? ''));
  protected readonly errorCount = computed(() => this.activity().filter((e) => e.type === 'WorkflowError').length);
  protected readonly isRunning = computed(() => this.summary()?.status === 'Runnable');
  protected readonly selectedPointers = computed(() => {
    const stepId = this.selectedStep();
    return stepId === null ? [] : this.pointers().filter((p) => p.stepId === stepId);
  });

  /** Time window the timeline bars are drawn against: creation until completion (or now). */
  private readonly span = computed(() => {
    const s = this.summary();
    if (!s) return null;
    const start = Date.parse(s.createTime);
    const end = s.completeTime ? Date.parse(s.completeTime) : this.now();
    const lastPointer = Math.max(...this.pointers().map((p) => Date.parse(p.endTime ?? p.startTime ?? s.createTime)));
    return { start, end: Math.max(end, lastPointer, start + 1) };
  });

  private reloadTimer?: ReturnType<typeof setTimeout>;

  constructor() {
    effect(() => {
      const id = this.id();
      this.detail.set(null);
      this.selectedStep.set(null);
      this.loading.set(true);
      void this.load(id);
    });

    inject(LiveService)
      .activity$.pipe(
        filter((e) => e.instanceId === this.id()),
        takeUntilDestroyed(),
      )
      .subscribe((e) => {
        const [list, isNew] = upsertActivity(this.activity(), e);
        this.activity.set(list);
        if (isNew) this.scheduleReload();
      });

    // Keeps durations and timeline bars of running steps moving.
    const ticker = setInterval(() => {
      if (this.summary()?.status === 'Runnable') this.now.set(Date.now());
    }, 5000);

    inject(DestroyRef).onDestroy(() => {
      clearInterval(ticker);
      clearTimeout(this.reloadTimer);
    });
  }

  protected duration(start: string | null, end: string | null): string {
    return durationBetween(start, end, this.now());
  }

  protected ago(value: string | null): string {
    return relativeTime(value, this.now());
  }

  /** A failed step that Workflow Core will run again shows as "Retrying" rather than "Failed". */
  protected displayStatus(p: PointerDto): string {
    return p.status === 'Failed' && p.active && p.sleepUntil ? 'Retrying' : p.status;
  }

  protected pointerDuration(p: PointerDto): string {
    const end = this.pointerEnd(p);
    return p.startTime && end !== null ? formatDuration(end - Date.parse(p.startTime)) : '—';
  }

  protected bar(p: PointerDto): TimelineBar | null {
    const span = this.span();
    if (!span || !p.startTime) return null;
    const total = span.end - span.start;
    const start = Date.parse(p.startTime);
    const end = this.pointerEnd(p) ?? start;
    return {
      left: Math.min(99.5, ((start - span.start) / total) * 100),
      width: Math.max(0.5, ((end - start) / total) * 100),
      tone: statusTone(this.displayStatus(p)),
    };
  }

  /** End time in ms; "now" for steps still in progress, null when a stopped step never finished. */
  private pointerEnd(p: PointerDto): number | null {
    if (p.endTime) return Date.parse(p.endTime);
    const inProgress = (p.active || p.status === 'WaitingForEvent') && this.summary()?.status === 'Runnable';
    return inProgress ? this.now() : null;
  }

  protected toggle(p: PointerDto): void {
    this.expanded.set(this.expanded() === p.id ? null : p.id);
  }

  protected async suspend(): Promise<void> {
    await this.act('Suspend', () => this.api.suspend(this.id()));
  }

  protected async resume(): Promise<void> {
    await this.act('Resume', () => this.api.resume(this.id()));
  }

  protected async terminate(): Promise<void> {
    const ok = await confirm(this.dialog, {
      title: 'Terminate workflow?',
      message: 'The instance stops permanently and cannot be resumed.',
      confirm: 'Terminate',
      danger: true,
    });
    if (ok) await this.act('Terminate', () => this.api.terminate(this.id()));
  }

  protected publishFor(p: PointerDto): void {
    this.dialog
      .open(PublishEventDialog, {
        width: '560px',
        maxWidth: '95vw',
        data: { eventName: p.eventName ?? '', eventKey: p.eventKey ?? '' },
      })
      .afterClosed()
      .subscribe((published?: boolean) => {
        if (published) this.scheduleReload();
      });
  }

  protected async copyId(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.id());
      this.snack.open('Instance ID copied', undefined, { duration: 2000 });
    } catch {
      // Clipboard unavailable; the ID is still selectable on the page.
    }
  }

  private async act(label: string, action: () => Promise<{ success: boolean }>): Promise<void> {
    this.busy.set(true);
    try {
      const result = await action();
      this.snack.open(result.success ? `${label} requested` : `${label} was not applied`, undefined, { duration: 3000 });
      await this.load(this.id());
    } catch (e) {
      this.snack.open(errorMessage(e), 'Dismiss', { duration: 6000 });
    } finally {
      this.busy.set(false);
    }
  }

  /** Activity is only fetched on first load; afterwards live events keep it current. */
  private async load(id: string, withActivity = true): Promise<void> {
    try {
      const [detail, activity] = await Promise.all([
        this.api.instance(id),
        withActivity ? this.api.activity({ instanceId: id, take: 1000 }) : Promise.resolve(null),
      ]);
      if (id !== this.id()) return;
      this.detail.set(detail);
      if (activity) this.activity.set(activity);
      this.error.set(null);
      this.now.set(Date.now());
      await this.loadDefinition(detail.summary.definitionId, detail.summary.version);
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }

  private async loadDefinition(id: string, version: number): Promise<void> {
    const current = this.definition();
    if (current?.id === id && current.version === version) return;
    try {
      this.definition.set(await this.api.definition(id, version));
    } catch {
      // The definition is no longer registered; the graph tab explains that.
      this.definition.set(null);
    }
  }

  // Step events arrive in bursts while a workflow runs; reload at most twice a second.
  private scheduleReload(): void {
    if (this.reloadTimer) return;
    this.reloadTimer = setTimeout(() => {
      this.reloadTimer = undefined;
      void this.load(this.id(), false);
    }, 500);
  }
}
