import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIcon } from '@angular/material/icon';
import { MatInput } from '@angular/material/input';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltip } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { ApiService, errorCode, errorMessage } from '../../core/api.service';
import { durationBetween, relativeTime, shortId } from '../../core/format';
import { LiveService } from '../../core/live.service';
import { InstanceQuery, InstanceSummary, WORKFLOW_STATUSES, WorkflowStatus } from '../../core/models';
import { StatusChip } from '../../shared/status-chip';

const PAGE_SIZES = [25, 50, 100];

@Component({
  selector: 'wfc-instances',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    FormsModule,
    RouterLink,
    MatButton,
    MatIconButton,
    MatIcon,
    MatFormFieldModule,
    MatInput,
    MatSelectModule,
    MatProgressBar,
    MatTooltip,
    StatusChip,
  ],
  templateUrl: './instances.html',
  styleUrl: './instances.scss',
})
export class InstancesPage {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);

  // Filters arrive as query params so a filtered list can be bookmarked or linked to.
  readonly status = input<string | undefined>();
  readonly definitionId = input<string | undefined>();
  readonly createdFrom = input<string | undefined>();
  readonly createdTo = input<string | undefined>();
  readonly skip = input<string | undefined>();
  readonly take = input<string | undefined>();

  protected readonly statuses = WORKFLOW_STATUSES;
  protected readonly pageSizes = PAGE_SIZES;
  protected readonly definitionIds = signal<string[]>([]);

  protected readonly items = signal<InstanceSummary[]>([]);
  protected readonly hasMore = signal(false);
  protected readonly total = signal<number | null>(null);
  protected readonly source = signal<'journal' | 'provider'>('journal');
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly listingUnsupported = signal(false);
  protected readonly lookupId = signal('');
  protected readonly lookupError = signal<string | null>(null);
  protected readonly newEvents = signal(0);

  protected readonly query = computed<InstanceQuery>(() => ({
    status: (WORKFLOW_STATUSES as string[]).includes(this.status() ?? '') ? (this.status() as WorkflowStatus) : null,
    definitionId: this.definitionId() || null,
    createdFrom: this.createdFrom() || null,
    createdTo: this.createdTo() || null,
    skip: Math.max(0, Number(this.skip()) || 0),
    take: PAGE_SIZES.includes(Number(this.take())) ? Number(this.take()) : PAGE_SIZES[0],
  }));

  protected readonly pageLabel = computed(() => {
    const q = this.query();
    const count = this.items().length;
    const total = this.total();
    const range = count ? `${q.skip + 1}–${q.skip + count}` : '0';
    return total === null ? range : `${range} of ${total}`;
  });

  constructor() {
    void this.api
      .definitions()
      .then((defs) => this.definitionIds.set([...new Set(defs.map((d) => d.id))]))
      .catch(() => undefined);

    effect(() => {
      void this.load(this.query());
    });

    // Workflow-level events mean the list may be stale. Show a hint rather than reshuffling rows under the cursor.
    inject(LiveService)
      .activity$.pipe(takeUntilDestroyed())
      .subscribe((e) => {
        if (!e.type.startsWith('Step')) this.newEvents.update((n) => n + 1);
      });
  }

  protected refresh(): void {
    void this.load(this.query());
  }

  protected setFilter(patch: Record<string, string | number | null>): void {
    void this.router.navigate([], {
      queryParams: { ...patch, skip: 'skip' in patch ? patch['skip'] : null },
      queryParamsHandling: 'merge',
    });
  }

  protected clearFilters(): void {
    void this.router.navigate([], { queryParams: {} });
  }

  protected page(direction: 1 | -1): void {
    const q = this.query();
    this.setFilter({ skip: Math.max(0, q.skip + direction * q.take) || null });
  }

  protected dateInput(value: string | null | undefined): string {
    return value ? value.slice(0, 10) : '';
  }

  protected async lookup(): Promise<void> {
    const id = this.lookupId().trim();
    if (!id) return;
    this.lookupError.set(null);
    try {
      await this.api.instance(id);
      void this.router.navigate(['/instances', id]);
    } catch (e) {
      this.lookupError.set(errorMessage(e));
    }
  }

  protected duration(wf: InstanceSummary): string {
    return durationBetween(wf.createTime, wf.completeTime);
  }

  protected ago(value: string): string {
    return relativeTime(value);
  }

  protected short(id: string): string {
    return shortId(id);
  }

  private async load(q: InstanceQuery): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      // Date inputs give whole days; make "to" inclusive of that day.
      const createdTo = q.createdTo ? `${q.createdTo}T23:59:59.999Z` : null;
      const createdFrom = q.createdFrom ? `${q.createdFrom}T00:00:00Z` : null;
      const page = await this.api.instances({ ...q, createdFrom, createdTo });
      // The journal index is newest first already; provider pages come in storage order, so sort within the page.
      const items = page.source === 'provider' ? [...page.items].sort((a, b) => b.createTime.localeCompare(a.createTime)) : page.items;
      this.items.set(items);
      this.hasMore.set(page.hasMore);
      this.total.set(page.total);
      this.source.set(page.source);
      this.listingUnsupported.set(false);
      this.newEvents.set(0);
    } catch (e) {
      this.items.set([]);
      this.hasMore.set(false);
      this.total.set(null);
      this.listingUnsupported.set(errorCode(e) === 'listing-not-supported');
      this.error.set(errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }
}
