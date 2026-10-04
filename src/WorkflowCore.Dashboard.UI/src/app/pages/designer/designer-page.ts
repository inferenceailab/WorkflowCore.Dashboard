import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, viewChild } from '@angular/core';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIcon } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltip } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { stringify } from 'yaml';
import { errorMessage } from '../../core/api.service';
import { StartWorkflowDialog } from '../../dialogs/start-workflow-dialog';
import { confirm } from '../../shared/confirm-dialog';
import { DesignerApi, PublishResponse, StepTypeInfo, ValidationIssue } from './designer-api.service';
import { DesignerCanvas } from './designer-canvas';
import { DesignerInspector } from './designer-inspector';
import { DesignerState } from './designer-state';
import { DesignerToolbox } from './designer-toolbox';
import { cleanForExport } from './dsl';

@Component({
  selector: 'wfc-designer-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [DesignerState],
  imports: [
    RouterLink,
    MatButton,
    MatIconButton,
    MatIcon,
    MatMenuModule,
    MatProgressBar,
    MatTooltip,
    DesignerCanvas,
    DesignerInspector,
    DesignerToolbox,
  ],
  templateUrl: './designer-page.html',
  styleUrl: './designer-page.scss',
  host: {
    '(window:keydown)': 'onShortcut($event)',
    '(window:beforeunload)': 'onBeforeUnload($event)',
  },
})
export class DesignerPage {
  private readonly api = inject(DesignerApi);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);
  private readonly router = inject(Router);
  protected readonly state = inject(DesignerState);
  private readonly canvas = viewChild(DesignerCanvas);

  readonly id = input.required<string>();

  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly issuesOpen = signal(true);

  protected readonly errors = computed(() => this.state.issues().filter((i) => i.severity === 'error').length);
  protected readonly warnings = computed(() => this.state.issues().length - this.errors());
  protected readonly status = computed(() => {
    if (this.state.dirty()) return { text: 'Unsaved changes', tone: 'suspended' };
    const latest = Math.max(0, ...this.state.versions().map((v) => v.version));
    if (this.state.hasDraft()) return { text: latest ? `Draft (v${latest} is live)` : 'Draft, not published', tone: 'pending' };
    return { text: `Published v${latest}`, tone: 'complete' };
  });

  constructor() {
    effect(() => void this.load(this.id()));
  }

  /** Used by the route's leave guard. */
  async canLeave(): Promise<boolean> {
    if (!this.state.dirty()) return true;
    return confirm(this.dialog, {
      title: 'Leave without saving?',
      message: 'Your changes since the last save will be lost.',
      confirm: 'Leave',
      danger: true,
    });
  }

  protected onBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.state.dirty()) event.preventDefault();
  }

  protected onShortcut(event: KeyboardEvent): void {
    if (!(event.ctrlKey || event.metaKey)) return;
    const typing = (event.target as HTMLElement | null)?.closest('input, textarea, select');
    const key = event.key.toLowerCase();
    if (key === 's') {
      event.preventDefault();
      void this.saveDraft();
    } else if (!typing && key === 'z' && !event.shiftKey) {
      event.preventDefault();
      this.state.undo();
    } else if (!typing && (key === 'y' || (key === 'z' && event.shiftKey))) {
      event.preventDefault();
      this.state.redo();
    }
  }

  protected addStep(type: StepTypeInfo): void {
    const canvas = this.canvas();
    if (canvas) this.state.addStep(type, canvas.newStepPosition());
  }

  protected async saveDraft(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    try {
      const { def, layout } = this.state.snapshot();
      await this.api.saveDraft(this.id(), def, layout);
      this.state.markSaved();
      this.state.hasDraft.set(true);
      this.snack.open('Draft saved', undefined, { duration: 2000 });
    } catch (e) {
      this.snack.open(`Could not save: ${errorMessage(e)}`, 'Dismiss', { duration: 6000 });
    } finally {
      this.busy.set(false);
    }
  }

  protected async validate(): Promise<void> {
    this.busy.set(true);
    try {
      const result = await this.api.validate(this.state.def());
      this.state.setIssues(result.issues);
      this.issuesOpen.set(true);
      this.snack.open(result.issues.length ? this.summary(result.issues) : 'No problems found', undefined, { duration: 3000 });
    } catch (e) {
      this.snack.open(errorMessage(e), 'Dismiss', { duration: 6000 });
    } finally {
      this.busy.set(false);
    }
  }

  protected async publish(): Promise<void> {
    this.busy.set(true);
    try {
      const { def, layout } = this.state.snapshot();
      const result = await this.api.publish(this.id(), def, layout);
      await this.reload();
      this.state.setIssues(result.validation.issues);
      this.snack
        .open(`Published version ${result.version}`, 'Start it', { duration: 8000 })
        .onAction()
        .subscribe(() => this.startVersion(result.version));
    } catch (e) {
      const rejected = e instanceof HttpErrorResponse && e.status === 422 ? (e.error as PublishResponse) : null;
      if (rejected) {
        this.state.setIssues(rejected.validation.issues);
        this.issuesOpen.set(true);
        this.snack.open(`Not published: ${this.summary(rejected.validation.issues)}`, 'Dismiss', { duration: 6000 });
      } else {
        this.snack.open(`Could not publish: ${errorMessage(e)}`, 'Dismiss', { duration: 6000 });
      }
    } finally {
      this.busy.set(false);
    }
  }

  protected async discardDraft(): Promise<void> {
    const published = this.state.versions().length > 0;
    const ok = await confirm(this.dialog, {
      title: published ? 'Discard draft?' : 'Delete this workflow?',
      message: published
        ? 'The editor goes back to the latest published version.'
        : 'It was never published, so nothing else uses it.',
      confirm: published ? 'Discard' : 'Delete',
      danger: true,
    });
    if (!ok) return;
    await this.api.discardDraft(this.id());
    if (published) await this.reload();
    else {
      this.state.markSaved();
      void this.router.navigate(['/designer']);
    }
  }

  protected export(format: 'json' | 'yaml'): void {
    const def = cleanForExport(this.state.def());
    // lineWidth 0: long type names stay on one line, as people write them by hand.
    const text = format === 'json' ? JSON.stringify(def, null, 2) : stringify(def, { lineWidth: 0 });
    const url = URL.createObjectURL(new Blob([text], { type: format === 'json' ? 'application/json' : 'application/yaml' }));
    const link = Object.assign(document.createElement('a'), { href: url, download: `${this.id()}.${format}` });
    link.click();
    URL.revokeObjectURL(url);
  }

  protected reveal(issue: ValidationIssue): void {
    if (issue.stepId) this.state.reveal(issue.stepId);
  }

  private async load(id: string): Promise<void> {
    this.loading.set(true);
    this.loadError.set(null);
    try {
      const [document, catalog] = await Promise.all([this.api.get(id), this.api.catalog()]);
      this.state.load(document, catalog);
    } catch (e) {
      this.loadError.set(errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }

  private async reload(): Promise<void> {
    const document = await this.api.get(this.id());
    const path = this.state.path();
    this.state.load(document, this.state.catalog());
    this.state.goTo(path);
  }

  private startVersion(version: number): void {
    this.dialog
      .open(StartWorkflowDialog, { width: '640px', maxWidth: '95vw', data: { definitionId: this.id(), version } })
      .afterClosed()
      .subscribe((id?: string) => {
        if (id) void this.router.navigate(['/instances', id]);
      });
  }

  private summary(issues: ValidationIssue[]): string {
    const errors = issues.filter((i) => i.severity === 'error').length;
    const warnings = issues.length - errors;
    const parts = [errors && `${errors} ${errors === 1 ? 'error' : 'errors'}`, warnings && `${warnings} ${warnings === 1 ? 'warning' : 'warnings'}`];
    return parts.filter(Boolean).join(', ');
  }
}
