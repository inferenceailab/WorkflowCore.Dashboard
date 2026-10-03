import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { MatSlideToggle } from '@angular/material/slide-toggle';
import { RouterLink } from '@angular/router';
import { upsertActivity } from '../../core/activity';
import { ApiService, errorCode, errorMessage } from '../../core/api.service';
import { relativeTime, shortId } from '../../core/format';
import { LiveService } from '../../core/live.service';
import { ActivityEntry, InstanceSummary } from '../../core/models';
import { ActivityList } from '../../shared/activity-list';

const FEED_LIMIT = 200;

interface Tile {
  type: string;
  label: string;
  icon: string;
  tone: string;
}

@Component({
  selector: 'wfc-home',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink, MatIcon, MatButton, MatSlideToggle, ActivityList],
  templateUrl: './home.html',
  styleUrl: './home.scss',
})
export class HomePage {
  private readonly api = inject(ApiService);
  private readonly live = inject(LiveService);

  protected readonly config = this.api.config;
  protected readonly totals = signal<Record<string, number>>({});
  protected readonly feed = signal<ActivityEntry[]>([]);
  protected readonly showSteps = signal(false);
  protected readonly running = signal<InstanceSummary[]>([]);
  protected readonly runningError = signal<string | null>(null);
  protected readonly definitionCount = signal<number | null>(null);

  protected readonly tiles: Tile[] = [
    { type: 'WorkflowStarted', label: 'Started', icon: 'play_circle', tone: 'runnable' },
    { type: 'WorkflowCompleted', label: 'Completed', icon: 'check_circle', tone: 'complete' },
    { type: 'WorkflowError', label: 'Step errors', icon: 'error', tone: 'failed' },
    { type: 'WorkflowSuspended', label: 'Suspended', icon: 'pause_circle', tone: 'suspended' },
    { type: 'WorkflowTerminated', label: 'Terminated', icon: 'cancel', tone: 'terminated' },
  ];

  protected readonly visibleFeed = computed(() =>
    this.showSteps() ? this.feed() : this.feed().filter((e) => !e.type.startsWith('Step')),
  );

  private refreshTimer?: ReturnType<typeof setTimeout>;

  constructor() {
    void this.load();

    this.live.activity$.pipe(takeUntilDestroyed()).subscribe((entry) => {
      const [feed, isNew] = upsertActivity(this.feed(), entry, FEED_LIMIT);
      this.feed.set(feed);
      if (!isNew) return;
      this.totals.update((t) => ({ ...t, [entry.type]: (t[entry.type] ?? 0) + 1 }));
      if (!entry.type.startsWith('Step')) this.scheduleRunningRefresh();
    });

    inject(DestroyRef).onDestroy(() => clearTimeout(this.refreshTimer));
  }

  protected total(type: string): number {
    return this.totals()[type] ?? 0;
  }

  protected ago(value: string | null): string {
    return relativeTime(value);
  }

  protected short(id: string): string {
    return shortId(id);
  }

  private async load(): Promise<void> {
    const [totals, feed, defs] = await Promise.allSettled([
      this.api.activityTotals(),
      this.api.activity(undefined, FEED_LIMIT),
      this.api.definitions(),
    ]);
    if (totals.status === 'fulfilled') this.totals.set(totals.value);
    if (feed.status === 'fulfilled') this.feed.set(feed.value);
    if (defs.status === 'fulfilled') this.definitionCount.set(new Set(defs.value.map((d) => d.id)).size);
    await this.loadRunning();
  }

  private async loadRunning(): Promise<void> {
    try {
      const page = await this.api.instances({ status: 'Runnable', skip: 0, take: 8 });
      this.running.set(page.items);
      this.runningError.set(null);
    } catch (e) {
      this.runningError.set(errorCode(e) === 'listing-not-supported' ? errorMessage(e) : `Could not load: ${errorMessage(e)}`);
    }
  }

  // Lifecycle events can arrive in bursts; refresh the running list at most every 2 seconds.
  private scheduleRunningRefresh(): void {
    if (this.refreshTimer) return;
    this.refreshTimer = setTimeout(() => {
      this.refreshTimer = undefined;
      void this.loadRunning();
    }, 2000);
  }
}
