import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIcon } from '@angular/material/icon';
import { MatInput } from '@angular/material/input';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { ApiService, errorMessage } from '../core/api.service';
import { DefinitionSummary, Json } from '../core/models';

export interface StartWorkflowData {
  definitionId?: string;
  version?: number;
}

@Component({
  selector: 'wfc-start-workflow-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatDialogModule, MatButton, MatFormFieldModule, MatInput, MatSelectModule, MatIcon, MatProgressBar],
  template: `
    <h2 mat-dialog-title>Start workflow</h2>
    <mat-dialog-content>
      @if (loading()) {
        <mat-progress-bar mode="indeterminate" />
      }
      <div class="row">
        <mat-form-field class="grow">
          <mat-label>Definition</mat-label>
          <mat-select [(ngModel)]="definitionId" required>
            @for (id of definitionIds(); track id) {
              <mat-option [value]="id">{{ id }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field class="version">
          <mat-label>Version</mat-label>
          <mat-select [(ngModel)]="version">
            @for (v of versions(); track v.version) {
              <mat-option [value]="v.version">v{{ v.version }}{{ v.isLatest ? ' (latest)' : '' }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
      </div>

      <mat-form-field class="full">
        <mat-label>Reference</mat-label>
        <input matInput [(ngModel)]="reference" placeholder="Optional business key, e.g. order-1042" />
      </mat-form-field>

      <mat-form-field class="full">
        <mat-label>Data (JSON)</mat-label>
        <textarea
          matInput
          class="code"
          rows="10"
          [ngModel]="dataText()"
          (ngModelChange)="dataText.set($event); dataEdited.set(true)"
          spellcheck="false"
        ></textarea>
        @if (dataType()) {
          <mat-hint>Deserialized into {{ dataType() }}</mat-hint>
        }
      </mat-form-field>

      @if (jsonError()) {
        <div class="error-banner"><mat-icon>error</mat-icon><span>{{ jsonError() }}</span></div>
      }
      @if (error()) {
        <div class="error-banner"><mat-icon>error</mat-icon><span>{{ error() }}</span></div>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button (click)="start()" [disabled]="!definitionId() || !!jsonError() || busy()">
        <mat-icon>play_arrow</mat-icon> Start
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .row { display: flex; gap: 12px; }
    .grow { flex: 1; min-width: 0; }
    .version { width: 150px; }
    .full { width: 100%; }
    textarea.code { font-family: var(--wfc-mono); font-size: 12.5px; line-height: 1.5; }
    .error-banner { margin-top: 4px; }
    mat-progress-bar { margin-bottom: 12px; }
    @media (max-width: 520px) { .row { flex-direction: column; gap: 0; } .version { width: 100%; } }
  `,
})
export class StartWorkflowDialog {
  private readonly api = inject(ApiService);
  private readonly ref = inject(MatDialogRef<StartWorkflowDialog, string>);
  private readonly input = inject<StartWorkflowData>(MAT_DIALOG_DATA, { optional: true }) ?? {};

  protected readonly definitions = signal<DefinitionSummary[]>([]);
  protected readonly definitionId = signal<string | null>(this.input.definitionId ?? null);
  protected readonly version = signal<number | null>(this.input.version ?? null);
  protected readonly reference = signal('');
  protected readonly dataText = signal('');
  protected readonly dataEdited = signal(false);
  protected readonly dataType = signal<string | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly definitionIds = computed(() => [...new Set(this.definitions().map((d) => d.id))]);
  protected readonly versions = computed(() => this.definitions().filter((d) => d.id === this.definitionId()));

  protected readonly jsonError = computed(() => {
    const text = this.dataText().trim();
    if (!text) return null;
    try {
      JSON.parse(text);
      return null;
    } catch (e) {
      return `Invalid JSON: ${(e as Error).message}`;
    }
  });

  constructor() {
    void this.api
      .definitions()
      .then((defs) => this.definitions.set(defs))
      .catch((e) => this.error.set(errorMessage(e)))
      .finally(() => this.loading.set(false));

    // Default to the latest version whenever the definition changes.
    effect(() => {
      const versions = this.versions();
      const current = this.version();
      if (versions.length && !versions.some((v) => v.version === current)) {
        this.version.set(versions.find((v) => v.isLatest)?.version ?? versions[0].version);
      }
    });

    // Prefill the data editor with the definition's data type until the user edits it.
    effect(() => {
      const id = this.definitionId();
      const version = this.version();
      if (!id || version === null) return;
      void this.api
        .definition(id, version)
        .then((def) => {
          this.dataType.set(def.dataType);
          if (!this.dataEdited()) {
            this.dataText.set(def.dataTemplate === null ? '' : JSON.stringify(def.dataTemplate, null, 2));
          }
        })
        .catch(() => this.dataType.set(null));
    });
  }

  protected async start(): Promise<void> {
    const id = this.definitionId();
    if (!id || this.jsonError()) return;

    this.busy.set(true);
    this.error.set(null);
    try {
      const text = this.dataText().trim();
      const data = text ? (JSON.parse(text) as Json) : undefined;
      const result = await this.api.startWorkflow(id, this.version(), data, this.reference().trim() || null);
      this.ref.close(result.id);
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
