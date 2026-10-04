import { ChangeDetectionStrategy, Component, computed, inject, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatIcon } from '@angular/material/icon';
import { MatTooltip } from '@angular/material/tooltip';
import { stepIcon } from '../../shared/workflow-graph/graph-model';
import { StepTypeInfo } from './designer-api.service';
import { STEP_MIME, shortTypeName } from './designer-canvas';
import { DesignerState } from './designer-state';

const BUILT_IN = ['Control flow', 'Timing', 'Events'];

@Component({
  selector: 'wfc-designer-toolbox',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatIcon, MatTooltip],
  template: `
    <div class="search">
      <mat-icon>search</mat-icon>
      <input [(ngModel)]="filter" placeholder="Find a step" aria-label="Find a step" />
    </div>
    <div class="groups">
      @for (group of groups(); track group.name) {
        <section>
          <h3>{{ group.name }}</h3>
          @for (type of group.types; track type.type) {
            <button
              class="item"
              draggable="true"
              (dragstart)="onDragStart(type, $event)"
              (click)="add.emit(type)"
              [matTooltip]="type.description ?? type.type"
              matTooltipPosition="right"
            >
              <mat-icon [class.container]="type.isContainer">{{ icon(type) }}</mat-icon>
              <span>{{ type.name }}</span>
            </button>
          }
        </section>
      } @empty {
        <p class="none">No step matches “{{ filter() }}”.</p>
      }
    </div>
  `,
  styles: `
    :host { display: flex; flex-direction: column; min-height: 0; background: var(--wfc-card); }
    .search {
      display: flex;
      align-items: center;
      gap: 6px;
      margin: 10px;
      padding: 0 10px;
      height: 36px;
      border: 1px solid var(--wfc-border);
      border-radius: 10px;
      mat-icon { color: var(--wfc-muted); font-size: 18px; width: 18px; height: 18px; }
      input { flex: 1; min-width: 0; border: 0; outline: 0; background: transparent; color: inherit; font: var(--mat-sys-body-medium); }
      &:focus-within { border-color: var(--mat-sys-primary); }
    }
    .groups { flex: 1; overflow-y: auto; padding: 0 6px 12px; }
    h3 {
      margin: 12px 8px 4px;
      font: var(--mat-sys-label-small);
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: var(--wfc-muted);
    }
    .item {
      display: flex;
      align-items: center;
      gap: 10px;
      width: 100%;
      padding: 7px 10px;
      border: 0;
      border-radius: 8px;
      background: none;
      color: var(--mat-sys-on-surface);
      font: var(--mat-sys-label-large);
      text-align: left;
      cursor: grab;
      mat-icon { color: var(--mat-sys-primary); font-size: 20px; width: 20px; height: 20px; }
      mat-icon.container { color: var(--mat-sys-tertiary); }
      &:hover { background: var(--mat-sys-surface-container); }
      &:focus-visible { outline: 2px solid var(--mat-sys-primary); }
    }
    .none { margin: 16px; color: var(--wfc-muted); font: var(--mat-sys-body-small); }
  `,
})
export class DesignerToolbox {
  private readonly state = inject(DesignerState);
  readonly add = output<StepTypeInfo>();
  protected readonly filter = signal('');

  protected readonly groups = computed(() => {
    const term = this.filter().trim().toLowerCase();
    const types = this.state
      .catalog()
      .steps.filter((t) => !term || t.name.toLowerCase().includes(term) || t.type.toLowerCase().includes(term));
    const names = [...new Set(types.map((t) => t.category))].sort((a, b) => {
      const ia = BUILT_IN.indexOf(a);
      const ib = BUILT_IN.indexOf(b);
      // App steps first: they are what most workflows are made of.
      if (ia === -1 && ib === -1) return a.localeCompare(b);
      if (ia === -1) return -1;
      if (ib === -1) return 1;
      return ia - ib;
    });
    return names.map((name) => ({ name, types: types.filter((t) => t.category === name) }));
  });

  protected icon(type: StepTypeInfo): string {
    return stepIcon(shortTypeName(type.type));
  }

  protected onDragStart(type: StepTypeInfo, event: DragEvent): void {
    event.dataTransfer?.setData(STEP_MIME, type.type);
    event.dataTransfer?.setData('text/plain', type.name);
    if (event.dataTransfer) event.dataTransfer.effectAllowed = 'copy';
  }
}
