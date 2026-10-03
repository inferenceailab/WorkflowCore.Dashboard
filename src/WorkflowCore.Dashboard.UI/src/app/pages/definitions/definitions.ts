import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatIconButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIcon } from '@angular/material/icon';
import { MatInput } from '@angular/material/input';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatTooltip } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { ApiService, errorMessage } from '../../core/api.service';
import { DefinitionSummary } from '../../core/models';
import { StartWorkflowDialog } from '../../dialogs/start-workflow-dialog';

interface DefinitionGroup {
  id: string;
  latest: DefinitionSummary;
  versions: number[];
}

@Component({
  selector: 'wfc-definitions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, MatIconButton, MatIcon, MatFormFieldModule, MatInput, MatProgressBar, MatTooltip],
  template: `
    <div class="heading">
      <div>
        <h1>Workflow definitions</h1>
        <p class="muted">Every workflow registered with the host, whether built in C# or loaded from JSON/YAML.</p>
      </div>
    </div>

    <section class="panel">
      <div class="panel-header compact-fields">
        <mat-form-field class="search" subscriptSizing="dynamic">
          <mat-icon matPrefix>search</mat-icon>
          <input matInput placeholder="Filter by id or description" [(ngModel)]="filter" />
        </mat-form-field>
        <span class="muted count">{{ groups().length }} workflows</span>
      </div>
      @if (loading()) {
        <mat-progress-bar mode="indeterminate" />
      }
      @if (error()) {
        <div class="error-banner"><mat-icon>error</mat-icon><span>{{ error() }}</span></div>
      }

      <div class="table-scroll">
        <table class="grid">
          <thead>
            <tr>
              <th>Definition</th>
              <th>Versions</th>
              <th>Data type</th>
              <th class="num">Steps</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            @for (g of groups(); track g.id) {
              <tr class="clickable" (click)="open(g)">
                <td>
                  <div class="name">{{ g.id }}</div>
                  @if (g.latest.description) {
                    <div class="muted desc">{{ g.latest.description }}</div>
                  }
                </td>
                <td>
                  <div class="versions">
                    @for (v of g.versions; track v) {
                      <a
                        class="version"
                        [class.latest]="v === g.latest.version"
                        [routerLink]="['/definitions', g.id, v]"
                        (click)="$event.stopPropagation()"
                        >v{{ v }}</a
                      >
                    }
                  </div>
                </td>
                <td class="mono muted type" [title]="g.latest.dataType ?? ''">{{ shortType(g.latest.dataType) }}</td>
                <td class="num">{{ g.latest.stepCount }}</td>
                <td class="actions">
                  @if (allowActions()) {
                    <button mat-icon-button matTooltip="Start" (click)="start(g); $event.stopPropagation()">
                      <mat-icon>play_arrow</mat-icon>
                    </button>
                  }
                  <a
                    mat-icon-button
                    matTooltip="Instances"
                    routerLink="/instances"
                    [queryParams]="{ definitionId: g.id }"
                    (click)="$event.stopPropagation()"
                  >
                    <mat-icon>format_list_bulleted</mat-icon>
                  </a>
                </td>
              </tr>
            } @empty {
              @if (!loading()) {
                <tr>
                  <td colspan="5" class="empty">No workflow definitions match.</td>
                </tr>
              }
            }
          </tbody>
        </table>
      </div>
    </section>
  `,
  styleUrl: './definitions.scss',
})
export class DefinitionsPage {
  private readonly api = inject(ApiService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);

  protected readonly allowActions = computed(() => this.api.config().allowActions);
  protected readonly definitions = signal<DefinitionSummary[]>([]);
  protected readonly filter = signal('');
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly groups = computed<DefinitionGroup[]>(() => {
    const term = this.filter().trim().toLowerCase();
    const byId = new Map<string, DefinitionSummary[]>();
    for (const d of this.definitions()) {
      if (term && !d.id.toLowerCase().includes(term) && !(d.description ?? '').toLowerCase().includes(term)) continue;
      byId.set(d.id, [...(byId.get(d.id) ?? []), d]);
    }
    return [...byId.entries()].map(([id, list]) => ({
      id,
      latest: list.find((d) => d.isLatest) ?? list[0],
      versions: list.map((d) => d.version).sort((a, b) => b - a),
    }));
  });

  constructor() {
    void this.api
      .definitions()
      .then((d) => this.definitions.set(d))
      .catch((e) => this.error.set(errorMessage(e)))
      .finally(() => this.loading.set(false));
  }

  protected shortType(type: string | null): string {
    if (!type) return '—';
    return type.split(/[.+]/).pop() ?? type;
  }

  protected open(g: DefinitionGroup): void {
    void this.router.navigate(['/definitions', g.id, g.latest.version]);
  }

  protected start(g: DefinitionGroup): void {
    this.dialog
      .open(StartWorkflowDialog, {
        width: '640px',
        maxWidth: '95vw',
        data: { definitionId: g.id, version: g.latest.version },
      })
      .afterClosed()
      .subscribe((id?: string) => {
        if (id) void this.router.navigate(['/instances', id]);
      });
  }
}
