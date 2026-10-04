import { ChangeDetectionStrategy, Component, computed, effect, inject, input, numberAttribute, signal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { Router, RouterLink } from '@angular/router';
import { ApiService, errorMessage } from '../../core/api.service';
import { DefinitionDetail, StepDto } from '../../core/models';
import { DesignerApi } from '../designer/designer-api.service';
import { StartWorkflowDialog } from '../../dialogs/start-workflow-dialog';
import { JsonView } from '../../shared/json-view';
import { StepPanel } from '../../shared/step-panel';
import { WorkflowGraph } from '../../shared/workflow-graph/workflow-graph';

@Component({
  selector: 'wfc-definition-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatButton, MatIcon, MatProgressBar, MatTabsModule, JsonView, StepPanel, WorkflowGraph],
  templateUrl: './definition-detail.html',
  styleUrl: './definition-detail.scss',
})
export class DefinitionDetailPage {
  private readonly api = inject(ApiService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);

  readonly id = input.required<string>();
  readonly version = input.required({ transform: numberAttribute });

  protected readonly allowActions = computed(() => this.api.config().allowActions);
  protected readonly def = signal<DefinitionDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly selected = signal<number | null>(null);
  /** True when this workflow was made in the designer, so it can be edited there. */
  protected readonly isDesign = signal(false);

  private readonly stepsById = computed(() => new Map((this.def()?.steps ?? []).map((s) => [s.id, s])));

  constructor() {
    const designer = inject(DesignerApi);
    effect(() => {
      const id = this.id();
      this.isDesign.set(false);
      if (this.api.hasFeature('designer')) {
        void designer
          .list()
          .then((designs) => this.isDesign.set(designs.some((d) => d.id === id)))
          .catch(() => undefined);
      }
    });

    effect(() => {
      const id = this.id();
      const version = this.version();
      this.loading.set(true);
      this.error.set(null);
      this.selected.set(null);
      void this.api
        .definition(id, version)
        .then((d) => this.def.set(d))
        .catch((e) => this.error.set(errorMessage(e)))
        .finally(() => this.loading.set(false));
    });
  }

  protected stepName(id: number): string {
    return this.stepsById().get(id)?.name ?? `#${id}`;
  }

  protected childNames(step: StepDto): string {
    return step.children.map((id) => this.stepName(id)).join(', ');
  }

  protected isContainer(step: StepDto): boolean {
    return step.children.length > 0;
  }

  protected start(): void {
    const def = this.def();
    if (!def) return;
    this.dialog
      .open(StartWorkflowDialog, { width: '640px', maxWidth: '95vw', data: { definitionId: def.id, version: def.version } })
      .afterClosed()
      .subscribe((id?: string) => {
        if (id) void this.router.navigate(['/instances', id]);
      });
  }
}
