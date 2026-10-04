import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import { Router, RouterLink } from '@angular/router';
import { ApiService, errorMessage } from '../../core/api.service';
import { relativeTime } from '../../core/format';
import { DesignerApi, DesignerSummary } from './designer-api.service';
import { NewDesignData, NewDesignDialog, NewDesignResult } from './new-design-dialog';

@Component({
  selector: 'wfc-designer-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink, MatButton, MatIcon, MatProgressBar],
  template: `
    <div class="heading">
      <div>
        <h1>Designer</h1>
        <p class="muted">Build JSON/YAML workflows visually. Publishing registers a new version you can start right away.</p>
      </div>
      <div class="heading-actions">
        <button mat-stroked-button (click)="open('import')"><mat-icon>upload</mat-icon> Import</button>
        <button mat-flat-button (click)="open('new')"><mat-icon>add</mat-icon> New workflow</button>
      </div>
    </div>

    @if (!persistent()) {
      <div class="note">
        <mat-icon>info</mat-icon>
        <span>
          Designs are kept in memory and lost when the app restarts. Configure a persistent journal
          (<code>UseEntityFrameworkJournal</code>) to keep them.
        </span>
      </div>
    }

    <section class="panel">
      @if (loading()) {
        <mat-progress-bar mode="indeterminate" />
      }
      @if (error()) {
        <div class="error-banner list-error"><mat-icon>error</mat-icon><span>{{ error() }}</span></div>
      }
      <div class="table-scroll">
        <table class="grid">
          <thead>
            <tr>
              <th>Workflow</th>
              <th>Status</th>
              <th>Updated</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            @for (d of designs(); track d.id) {
              <tr class="clickable" [routerLink]="['/designer', d.id]">
                <td>
                  <div class="name">{{ d.id }}</div>
                  @if (d.description) {
                    <div class="muted desc">{{ d.description }}</div>
                  }
                </td>
                <td>
                  @if (d.latestVersion) {
                    <span class="badge live">v{{ d.latestVersion }} live</span>
                  }
                  @if (d.hasDraft) {
                    <span class="badge draft">Draft</span>
                  }
                </td>
                <td [title]="d.updatedAt | date: 'medium'">{{ ago(d.updatedAt) }}</td>
                <td class="actions">
                  <a mat-button [routerLink]="['/designer', d.id]" (click)="$event.stopPropagation()">
                    <mat-icon>edit</mat-icon> Edit
                  </a>
                </td>
              </tr>
            } @empty {
              @if (!loading()) {
                <tr>
                  <td colspan="4" class="empty">
                    No designs yet. Create one, or import a JSON/YAML definition to edit it here.
                  </td>
                </tr>
              }
            }
          </tbody>
        </table>
      </div>
    </section>
  `,
  styles: `
    .note {
      display: flex;
      gap: 10px;
      align-items: flex-start;
      margin-bottom: 16px;
      padding: 12px 16px;
      border-radius: 10px;
      background: color-mix(in srgb, var(--wfc-runnable) 10%, transparent);
      font: var(--mat-sys-body-medium);
      mat-icon { color: var(--wfc-runnable); flex: none; }
      code { font-family: var(--wfc-mono); font-size: 0.9em; }
    }
    .list-error { margin: 12px 16px; }
    .name { font: var(--mat-sys-label-large); }
    .desc { font: var(--mat-sys-body-small); margin-top: 2px; }
    .badge {
      display: inline-block;
      margin-right: 6px;
      padding: 1px 9px;
      border-radius: 999px;
      font: var(--mat-sys-label-medium);
      &.live { color: var(--wfc-complete); background: color-mix(in srgb, var(--wfc-complete) 13%, transparent); }
      &.draft { color: var(--wfc-suspended); background: color-mix(in srgb, var(--wfc-suspended) 14%, transparent); }
    }
    .actions { text-align: right; width: 1%; white-space: nowrap; }
  `,
})
export class DesignerListPage {
  private readonly designer = inject(DesignerApi);
  private readonly api = inject(ApiService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);

  protected readonly designs = signal<DesignerSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly persistent = computed(() => this.api.config().journal.persistent);

  constructor() {
    void this.load();
  }

  protected ago(value: string): string {
    return relativeTime(value);
  }

  protected async open(mode: 'new' | 'import'): Promise<void> {
    const [catalog, definitions] = await Promise.all([this.designer.catalog(), this.api.definitions()]);
    const data: NewDesignData = {
      mode,
      designs: this.designs().map((d) => d.id),
      registered: [...new Set(definitions.map((d) => d.id))],
      dataTypes: catalog.dataTypes,
    };
    this.dialog
      .open(NewDesignDialog, { data, width: '620px', maxWidth: '95vw' })
      .afterClosed()
      .subscribe(async (result?: NewDesignResult) => {
        if (!result) return;
        try {
          await this.designer.saveDraft(result.id, result.source, {});
          void this.router.navigate(['/designer', result.id]);
        } catch (e) {
          this.error.set(errorMessage(e));
        }
      });
  }

  private async load(): Promise<void> {
    try {
      this.designs.set(await this.designer.list());
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }
}
