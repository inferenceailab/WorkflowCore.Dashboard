import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { MatTooltip } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { stepIcon } from '../../shared/workflow-graph/graph-model';
import { CatalogProperty } from './designer-api.service';
import { shortTypeName } from './designer-canvas';
import { DesignerState } from './designer-state';
import { ERROR_BEHAVIORS, ErrorBehavior, allSteps } from './dsl';

const PLACEHOLDERS: Record<CatalogProperty['kind'], string> = {
  string: '"text" or data.Name',
  bool: 'true or data.Flag',
  number: '42 or data.Count',
  timespan: 'TimeSpan.FromMinutes(5)',
  datetime: 'DateTime.Now',
  collection: 'data.Items',
  object: 'data.Value',
};

interface InputRow {
  /** Step ID + name: switching steps must not reuse a field that still holds the other step's text. */
  key: string;
  name: string;
  type: string;
  kind: CatalogProperty['kind'];
  value: string;
  /** Inputs that are objects rather than expressions, e.g. from an imported definition. Shown read-only. */
  complex: boolean;
  known: boolean;
}

@Component({
  selector: 'wfc-designer-inspector',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink, MatButton, MatIconButton, MatIcon, MatTooltip],
  templateUrl: './designer-inspector.html',
  styleUrl: './designer-inspector.scss',
})
export class DesignerInspector {
  protected readonly state = inject(DesignerState);
  protected readonly behaviors = ERROR_BEHAVIORS;

  protected readonly step = this.state.selectedStep;
  protected readonly type = computed(() => {
    const step = this.step();
    return step ? this.state.typeOf(step) : null;
  });
  protected readonly icon = computed(() => stepIcon(shortTypeName(this.step()?.StepType ?? '')));
  protected readonly stepIssues = computed(() => this.state.issuesByStep().get(this.step()?.Id ?? '') ?? []);

  protected readonly edge = computed(() => {
    const sel = this.state.selection();
    if (sel?.kind !== 'edge') return null;
    const steps = allSteps(this.state.def());
    const from = steps.find((s) => s.step.Id === sel.from)?.step;
    const to = steps.find((s) => s.step.Id === sel.to)?.step;
    if (!from || !to) return null;
    const condition = from.NextStepId === sel.to ? '' : (from.SelectNextStep?.[sel.to] ?? '');
    return { from: sel.from, to: sel.to, fromName: from.Name || from.Id, toName: to.Name || to.Id, condition };
  });

  protected readonly inputs = computed<InputRow[]>(() => {
    const step = this.step();
    if (!step) return [];
    const values = step.Inputs ?? {};
    const known = (this.type()?.inputs ?? []).map((p) => this.inputRow(step.Id, p.name, p.type, p.kind, values[p.name], true));
    const extra = Object.keys(values)
      .filter((k) => !known.some((r) => r.name === k))
      .map((k) => this.inputRow(step.Id, k, '?', 'object', values[k], false));
    return [...known, ...extra];
  });

  protected readonly outputs = computed(() => {
    const step = this.step();
    return Object.entries(step?.Outputs ?? {}).map(([property, expression], i) => ({ key: `${step!.Id}:${i}`, property, expression }));
  });
  /** Step outputs that are not stored yet, offered as one-click additions. */
  protected readonly suggestedOutputs = computed(() => {
    const used = new Set(Object.values(this.step()?.Outputs ?? {}));
    return (this.type()?.outputs ?? []).filter((o) => !used.has(`step.${o.name}`));
  });
  protected readonly dataProperties = computed(() => this.state.dataType()?.properties ?? []);

  /** The ID field is edited freely and applied on Enter or blur, since renaming rewrites connections. */
  protected readonly idDraft = signal('');
  protected readonly idError = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.idDraft.set(this.step()?.Id ?? '');
      this.idError.set(null);
    });
  }

  protected placeholder(kind: CatalogProperty['kind']): string {
    return PLACEHOLDERS[kind];
  }

  protected commitId(): void {
    const step = this.step();
    const next = this.idDraft().trim();
    if (!step || next === step.Id) return;
    if (!/^[A-Za-z_][A-Za-z0-9_.-]*$/.test(next)) {
      this.idError.set('Use letters, digits, “_”, “.” or “-”, starting with a letter.');
      return;
    }
    if (!this.state.renameStep(step.Id, next)) this.idError.set(`Another step already uses “${next}”.`);
  }

  protected setName(value: string): void {
    const step = this.step();
    if (step) this.state.updateStep(step.Id, 'name', (s) => (s.Name = value || null));
  }

  protected setInput(name: string, value: string): void {
    const step = this.step();
    if (!step) return;
    this.state.updateStep(step.Id, `input:${name}`, (s) => {
      const inputs = { ...(s.Inputs ?? {}) };
      if (value.trim()) inputs[name] = value;
      else delete inputs[name];
      s.Inputs = inputs;
    });
  }

  protected removeInput(name: string): void {
    const step = this.step();
    if (!step) return;
    this.state.updateStep(step.Id, `remove-input:${name}`, (s) => {
      const inputs = { ...(s.Inputs ?? {}) };
      delete inputs[name];
      s.Inputs = inputs;
    });
  }

  protected setOutput(index: number, property: string | null, expression: string | null): void {
    const step = this.step();
    if (!step) return;
    this.state.updateStep(step.Id, `output:${index}`, (s) => {
      const entries = Object.entries(s.Outputs ?? {});
      const [oldProperty, oldExpression] = entries[index];
      entries[index] = [property ?? oldProperty, expression ?? oldExpression];
      s.Outputs = Object.fromEntries(entries);
    });
  }

  protected addOutput(output?: CatalogProperty): void {
    const step = this.step();
    if (!step) return;
    this.state.updateStep(step.Id, 'add-output', (s) => {
      const outputs = { ...(s.Outputs ?? {}) };
      // Prefer a data property with the same name as the step output.
      let property = output && this.dataProperties().some((p) => p.name === output.name) ? output.name : 'Property';
      for (let i = 2; property in outputs; i++) property = `${output?.name ?? 'Property'}${i}`;
      outputs[property] = output ? `step.${output.name}` : 'step.';
      s.Outputs = outputs;
    });
  }

  protected removeOutput(property: string): void {
    const step = this.step();
    if (!step) return;
    this.state.updateStep(step.Id, `remove-output:${property}`, (s) => {
      const outputs = { ...(s.Outputs ?? {}) };
      delete outputs[property];
      s.Outputs = outputs;
    });
  }

  protected setErrorBehavior(value: string): void {
    const step = this.step();
    if (step) this.state.updateStep(step.Id, 'error-behavior', (s) => (s.ErrorBehavior = (value || null) as ErrorBehavior | null));
  }

  protected setRetryInterval(value: string): void {
    const step = this.step();
    if (step) this.state.updateStep(step.Id, 'retry-interval', (s) => (s.RetryInterval = value.trim() || null));
  }

  protected setCancelCondition(value: string): void {
    const step = this.step();
    if (step) this.state.updateStep(step.Id, 'cancel-condition', (s) => (s.CancelCondition = value.trim() || null));
  }

  protected setProceedOnCancel(value: boolean): void {
    const step = this.step();
    if (step) this.state.updateStep(step.Id, 'proceed-on-cancel', (s) => (s.ProceedOnCancel = value));
  }

  protected setCondition(value: string): void {
    const edge = this.edge();
    if (edge) this.state.setCondition(edge.from, edge.to, value);
  }

  protected setDescription(value: string): void {
    this.state.updateDefinition('description', (d) => (d.Description = value || null));
  }

  protected setDataType(value: string): void {
    this.state.updateDefinition('data-type', (d) => (d.DataType = value.trim() || null));
  }

  protected setDefaultErrorBehavior(value: string): void {
    this.state.updateDefinition('default-error-behavior', (d) => (d.DefaultErrorBehavior = (value || undefined) as ErrorBehavior | undefined));
  }

  protected setDefaultRetryInterval(value: string): void {
    this.state.updateDefinition('default-retry-interval', (d) => (d.DefaultErrorRetryInterval = value.trim() || null));
  }

  protected branchSize(index: number): number {
    return this.step()?.Do?.[index]?.length ?? 0;
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement).value;
  }

  protected checked(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }

  private inputRow(stepId: string, name: string, type: string, kind: CatalogProperty['kind'], value: unknown, known: boolean): InputRow {
    const complex = value !== undefined && typeof value !== 'string';
    return { key: `${stepId}:${name}`, name, type, kind, value: complex ? JSON.stringify(value) : ((value as string) ?? ''), complex, known };
  }
}
