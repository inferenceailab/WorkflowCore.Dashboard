import { ChangeDetectionStrategy, Component, computed, effect, inject, input, numberAttribute, signal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import { Router, RouterLink } from '@angular/router';
import { ApiService, errorMessage } from '../../core/api.service';
import { DefinitionDetail, StepDto } from '../../core/models';
import { StartWorkflowDialog } from '../../dialogs/start-workflow-dialog';
import { JsonView } from '../../shared/json-view';

@Component({
  selector: 'wfc-definition-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatButton, MatIcon, MatProgressBar, JsonView],
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

  private readonly stepsById = computed(() => new Map((this.def()?.steps ?? []).map((s) => [s.id, s])));

  constructor() {
    effect(() => {
      const id = this.id();
      const version = this.version();
      this.loading.set(true);
      this.error.set(null);
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
