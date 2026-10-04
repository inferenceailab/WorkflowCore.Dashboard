import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIcon } from '@angular/material/icon';
import { MatInput } from '@angular/material/input';
import { errorMessage } from '../../core/api.service';
import { DataTypeInfo, DesignerApi } from './designer-api.service';
import { DslDefinition } from './dsl';

export interface NewDesignData {
  mode: 'new' | 'import';
  /** IDs already used by designer workflows. */
  designs: string[];
  /** IDs of every registered workflow, including ones defined in code. */
  registered: string[];
  dataTypes: DataTypeInfo[];
}

export interface NewDesignResult {
  id: string;
  source: DslDefinition;
}

const ID_PATTERN = /^[A-Za-z][A-Za-z0-9_.-]{0,59}$/;

@Component({
  selector: 'wfc-new-design-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatDialogModule, MatButton, MatFormFieldModule, MatInput, MatIcon],
  template: `
    <h2 mat-dialog-title>{{ data.mode === 'new' ? 'New workflow' : 'Import a definition' }}</h2>
    <mat-dialog-content>
      @if (data.mode === 'import') {
        <p class="muted intro">Paste a Workflow Core JSON or YAML definition, or choose a file.</p>
        <div class="file-row">
          <button mat-stroked-button type="button" (click)="file.click()"><mat-icon>upload_file</mat-icon> Choose file</button>
          <input #file type="file" accept=".json,.yaml,.yml,application/json,text/yaml" hidden (change)="readFile($event)" />
          @if (fileName()) {
            <span class="muted">{{ fileName() }}</span>
          }
        </div>
        <mat-form-field class="full">
          <mat-label>Definition</mat-label>
          <textarea matInput class="code" rows="9" spellcheck="false" [(ngModel)]="text" (ngModelChange)="parsed.set(null)"></textarea>
        </mat-form-field>
        @if (!parsed()) {
          <button mat-stroked-button type="button" (click)="parse()" [disabled]="!text().trim() || busy()">
            <mat-icon>preview</mat-icon> Read definition
          </button>
        }
      }

      @if (data.mode === 'new' || parsed()) {
        <mat-form-field class="full">
          <mat-label>Workflow ID</mat-label>
          <input matInput class="mono" [(ngModel)]="id" required placeholder="OrderApproval" />
          <mat-hint>Letters, digits, “.”, “-” or “_”. Instances refer to it, so pick it carefully.</mat-hint>
        </mat-form-field>
        @if (data.mode === 'new') {
          <mat-form-field class="full">
            <mat-label>Data type</mat-label>
            <input matInput class="mono" list="wfc-new-data-types" [(ngModel)]="dataType" placeholder="MyApp.OrderData, MyApp" />
            <mat-hint>Optional. The class that holds the workflow's data.</mat-hint>
          </mat-form-field>
          <datalist id="wfc-new-data-types">
            @for (d of data.dataTypes; track d.type) {
              <option [value]="d.type">{{ d.name }}</option>
            }
          </datalist>
          <mat-form-field class="full">
            <mat-label>Description</mat-label>
            <input matInput [(ngModel)]="description" />
          </mat-form-field>
        } @else {
          <p class="muted">{{ parsed()!.Steps.length }} top-level steps read{{ parsed()!.DataType ? ', data type ' + parsed()!.DataType : '' }}.</p>
        }
        @if (idProblem(); as problem) {
          <div class="note" [class.blocking]="problem.blocking"><mat-icon>{{ problem.blocking ? 'error' : 'info' }}</mat-icon><span>{{ problem.text }}</span></div>
        }
      }

      @if (error()) {
        <div class="error-banner"><mat-icon>error</mat-icon><span>{{ error() }}</span></div>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button (click)="create()" [disabled]="!canCreate()">
        {{ data.mode === 'new' ? 'Create' : 'Import' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .full { width: 100%; }
    .intro { margin: 0 0 12px; }
    .file-row { display: flex; align-items: center; gap: 12px; margin-bottom: 12px; }
    textarea.code, .mono { font-family: var(--wfc-mono); font-size: 12.5px; }
    .note { display: flex; gap: 8px; align-items: flex-start; margin: 4px 0 8px; font: var(--mat-sys-body-small); color: var(--wfc-muted);
      mat-icon { font-size: 18px; width: 18px; height: 18px; } }
    .note.blocking { color: var(--wfc-failed); }
    .error-banner { margin-top: 8px; }
  `,
})
export class NewDesignDialog {
  protected readonly data = inject<NewDesignData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<NewDesignDialog, NewDesignResult>);
  private readonly api = inject(DesignerApi);

  protected readonly id = signal('');
  protected readonly dataType = signal('');
  protected readonly description = signal('');
  protected readonly text = signal('');
  protected readonly fileName = signal('');
  protected readonly parsed = signal<DslDefinition | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly idProblem = computed(() => {
    const id = this.id().trim();
    if (!id) return null;
    if (!ID_PATTERN.test(id)) return { blocking: true, text: 'Use 1–60 letters, digits, “.”, “-” or “_”, starting with a letter.' };
    if (this.data.designs.includes(id)) return { blocking: true, text: `A design called “${id}” already exists. Open it from the list instead.` };
    if (this.data.registered.includes(id))
      return { blocking: false, text: `“${id}” is already registered (from code or a file). Publishing adds new versions after the existing ones.` };
    return null;
  });

  protected readonly canCreate = computed(
    () => !!this.id().trim() && !this.idProblem()?.blocking && !this.busy() && (this.data.mode === 'new' || !!this.parsed()),
  );

  protected async readFile(event: Event): Promise<void> {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    this.fileName.set(file.name);
    this.text.set(await file.text());
    await this.parse();
  }

  /** The server reads the text with Workflow Core's own JSON/YAML deserializers. */
  protected async parse(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const { source } = await this.api.import(this.text());
      this.parsed.set(source);
      if (!this.id()) this.id.set(source.Id ?? '');
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected create(): void {
    const id = this.id().trim();
    const source: DslDefinition =
      this.data.mode === 'new'
        ? { Id: id, Description: this.description().trim() || null, DataType: this.dataType().trim() || null, Steps: [] }
        : { ...this.parsed()!, Id: id };
    this.ref.close({ id, source });
  }
}
