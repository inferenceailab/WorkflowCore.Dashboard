import { Injectable, computed, signal } from '@angular/core';
import { Graph, layout as dagreLayout } from '@dagrejs/dagre';
import { DataTypeInfo, DesignerDocument, DesignerVersion, StepCatalog, StepTypeInfo, ValidationIssue } from './designer-api.service';
import {
  DslDefinition,
  DslStep,
  Layout,
  Position,
  START,
  ScopeRef,
  allSteps,
  clone,
  connect,
  disconnect,
  edgesOf,
  removeStep,
  renameStep,
  scopeSteps,
  setCondition,
  startKey,
  uniqueId,
} from './dsl';

export const NODE_WIDTH = 220;
export const NODE_HEIGHT = 56;
export const START_WIDTH = 96;
export const START_HEIGHT = 32;

export type Selection = { kind: 'step'; id: string } | { kind: 'edge'; from: string; to: string } | null;

interface Snapshot {
  def: DslDefinition;
  layout: Layout;
}

interface UndoEntry {
  snapshot: string;
  key: string | null;
  at: number;
}

const UNDO_LIMIT = 100;
/** Edits with the same key this close together (typing in one field) undo as one step. */
const COALESCE_MS = 1500;

/** Editor state for one designer session. Provided by the designer page, so each visit starts fresh. */
@Injectable()
export class DesignerState {
  readonly id = signal('');
  readonly def = signal<DslDefinition>({ Id: '', Steps: [] });
  readonly layout = signal<Layout>({});
  readonly path = signal<ScopeRef[]>([]);
  readonly selection = signal<Selection>(null);
  readonly catalog = signal<StepCatalog>({ steps: [], dataTypes: [] });
  readonly issues = signal<ValidationIssue[]>([]);
  readonly versions = signal<DesignerVersion[]>([]);
  readonly hasDraft = signal(false);

  private readonly saved = signal('');
  private readonly validated = signal('');
  private readonly undoStack = signal<UndoEntry[]>([]);
  private readonly redoStack = signal<string[]>([]);
  private gesture: string | null = null;

  readonly canUndo = computed(() => this.undoStack().length > 0);
  readonly canRedo = computed(() => this.redoStack().length > 0);
  readonly dirty = computed(() => this.snapshotText() !== this.saved());
  /** True when the workflow changed after the issues were found. */
  readonly issuesStale = computed(() => this.issues().length > 0 && JSON.stringify(this.def()) !== this.validated());

  /** Steps of the scope being edited (root, or one branch of a container). */
  readonly steps = computed(() => scopeSteps(this.def(), this.path()) ?? []);
  readonly stepTypes = computed(() => new Map(this.catalog().steps.map((s) => [s.type, s])));
  readonly dataType = computed<DataTypeInfo | null>(
    () => this.catalog().dataTypes.find((d) => d.type === this.def().DataType) ?? null,
  );
  readonly issuesByStep = computed(() => {
    const map = new Map<string, ValidationIssue[]>();
    for (const issue of this.issues()) if (issue.stepId) map.set(issue.stepId, [...(map.get(issue.stepId) ?? []), issue]);
    return map;
  });
  readonly selectedStep = computed(() => {
    const sel = this.selection();
    return sel?.kind === 'step' ? (allSteps(this.def()).find((s) => s.step.Id === sel.id)?.step ?? null) : null;
  });
  /** Breadcrumb: the root, then each container branch entered. */
  readonly breadcrumb = computed(() => {
    const crumbs = [{ label: this.def().Id || 'Workflow', path: [] as ScopeRef[] }];
    const path = this.path();
    for (let i = 0; i < path.length; i++) {
      const step = allSteps(this.def()).find((s) => s.step.Id === path[i].stepId)?.step;
      const branches = step?.Do?.length ?? 1;
      const name = step?.Name || path[i].stepId;
      crumbs.push({ label: branches > 1 ? `${name} › branch ${path[i].branch + 1}` : name, path: path.slice(0, i + 1) });
    }
    return crumbs;
  });

  typeOf(step: DslStep): StepTypeInfo | null {
    return this.stepTypes().get(step.StepType) ?? null;
  }

  load(document: DesignerDocument, catalog: StepCatalog): void {
    const latest = [...document.versions].sort((a, b) => b.version - a.version)[0];
    const source = document.draft?.source ?? latest?.source ?? { Id: document.id, Steps: [] };
    const layout = document.draft?.layout ?? latest?.layout ?? {};
    this.catalog.set(catalog);
    this.id.set(document.id);
    this.versions.set(document.versions);
    this.hasDraft.set(document.draft !== null);
    this.def.set(normalize(clone(source), document.id));
    this.layout.set(clone(layout));
    this.path.set([]);
    this.selection.set(null);
    this.issues.set([]);
    this.layoutMissing();
    this.markSaved();
    this.undoStack.set([]);
    this.redoStack.set([]);
  }

  setIssues(issues: ValidationIssue[]): void {
    this.issues.set(issues);
    this.validated.set(JSON.stringify(this.def()));
  }

  markSaved(): void {
    this.saved.set(this.snapshotText());
  }

  snapshot(): Snapshot {
    return { def: this.def(), layout: this.layout() };
  }

  // ---- undo / redo -------------------------------------------------------------------------

  /** Applies an edit to copies of the document and layout. Returning false cancels it. */
  edit(key: string | null, change: (def: DslDefinition, layout: Layout) => boolean | void): boolean {
    const before = this.snapshotText();
    const def = clone(this.def());
    const layout = clone(this.layout());
    if (change(def, layout) === false) return false;

    const stack = this.undoStack();
    const last = stack[stack.length - 1];
    const now = Date.now();
    if (!(key && last?.key === key && now - last.at < COALESCE_MS)) {
      this.undoStack.set([...stack, { snapshot: before, key, at: now }].slice(-UNDO_LIMIT));
    } else {
      this.undoStack.set([...stack.slice(0, -1), { ...last, at: now }]);
    }
    this.redoStack.set([]);
    this.def.set(def);
    this.layout.set(layout);
    return true;
  }

  /** Drags update the layout continuously; the whole drag undoes as one step. */
  beginGesture(): void {
    this.gesture = this.snapshotText();
  }

  moveDuringGesture(id: string, position: Position): void {
    this.layout.update((l) => ({ ...l, [id]: position }));
  }

  endGesture(): void {
    if (this.gesture !== null && this.gesture !== this.snapshotText()) {
      this.undoStack.set([...this.undoStack(), { snapshot: this.gesture, key: null, at: Date.now() }].slice(-UNDO_LIMIT));
      this.redoStack.set([]);
    }
    this.gesture = null;
  }

  undo(): void {
    const stack = this.undoStack();
    if (!stack.length) return;
    this.redoStack.set([...this.redoStack(), this.snapshotText()]);
    this.undoStack.set(stack.slice(0, -1));
    this.restore(stack[stack.length - 1].snapshot);
  }

  redo(): void {
    const stack = this.redoStack();
    if (!stack.length) return;
    this.undoStack.set([...this.undoStack(), { snapshot: this.snapshotText(), key: null, at: 0 }]);
    this.redoStack.set(stack.slice(0, -1));
    this.restore(stack[stack.length - 1]);
  }

  // ---- edits -------------------------------------------------------------------------------

  addStep(type: StepTypeInfo, position: Position): string {
    let id = '';
    this.edit(null, (def, layout) => {
      const list = scopeSteps(def, this.path());
      if (!list) return false;
      id = uniqueId(def, type.name);
      const step: DslStep = { Id: id, StepType: type.type, Name: type.name };
      if (type.isContainer) step.Do = type.multipleBranches ? [[], []] : [[]];
      list.push(step);
      layout[id] = position;
      if (list.length === 1) layout[startKey(this.path())] = startPositionFor(position);
      return true;
    });
    this.selection.set({ kind: 'step', id });
    return id;
  }

  connect(from: string, to: string): void {
    this.edit(null, (def) => connect(def, this.path(), from, to));
  }

  disconnect(from: string, to: string): void {
    this.edit(null, (def) => disconnect(def, from, to));
    this.selection.set(null);
  }

  setCondition(from: string, to: string, condition: string): void {
    this.edit(`condition:${from}:${to}`, (def) => setCondition(def, from, to, condition));
  }

  removeStep(id: string): void {
    this.edit(null, (def, layout) => {
      for (const removed of removeStep(def, id)) delete layout[removed];
    });
    this.selection.set(null);
  }

  deleteSelection(): void {
    const sel = this.selection();
    if (sel?.kind === 'step') this.removeStep(sel.id);
    else if (sel?.kind === 'edge') this.disconnect(sel.from, sel.to);
  }

  renameStep(oldId: string, newId: string): boolean {
    const ok = this.edit(null, (def, layout) => {
      if (!renameStep(def, oldId, newId)) return false;
      if (layout[oldId]) {
        layout[newId] = layout[oldId];
        delete layout[oldId];
      }
      return true;
    });
    if (ok) {
      this.path.update((p) => p.map((r) => (r.stepId === oldId ? { ...r, stepId: newId } : r)));
      this.selection.set({ kind: 'step', id: newId });
    }
    return ok;
  }

  /** `key` groups keystrokes in one field into a single undo step. */
  updateStep(id: string, key: string, change: (step: DslStep) => void): void {
    this.edit(`${key}:${id}`, (def) => {
      const step = allSteps(def).find((s) => s.step.Id === id)?.step;
      if (!step) return false;
      change(step);
      return true;
    });
  }

  updateDefinition(key: string, change: (def: DslDefinition) => void): void {
    this.edit(`definition:${key}`, (def) => change(def));
  }

  addBranch(containerId: string): void {
    this.updateStep(containerId, 'branches', (step) => (step.Do = [...(step.Do ?? []), []]));
  }

  removeBranch(containerId: string, index: number): void {
    this.edit(null, (def, layout) => {
      const step = allSteps(def).find((s) => s.step.Id === containerId)?.step;
      if (!step?.Do || step.Do.length <= 1) return false;
      for (const s of step.Do[index]) for (const removed of removeStep(def, s.Id)) delete layout[removed];
      step.Do.splice(index, 1);
      return true;
    });
  }

  openBranch(containerId: string, branch: number): void {
    this.path.update((p) => [...p, { stepId: containerId, branch }]);
    this.selection.set(null);
  }

  goTo(path: ScopeRef[]): void {
    this.path.set(path);
    this.selection.set(null);
  }

  /** Opens the scope that contains a step and selects it, e.g. from the issues list. */
  reveal(stepId: string): void {
    const entry = allSteps(this.def()).find((s) => s.step.Id === stepId);
    if (!entry) return;
    this.path.set(entry.path);
    this.selection.set({ kind: 'step', id: stepId });
  }

  autoArrange(): void {
    this.edit(null, (def, layout) => {
      const list = scopeSteps(def, this.path());
      if (!list?.length) return false;
      Object.assign(layout, arrange(list, this.path()));
      return true;
    });
  }

  private layoutMissing(): void {
    const def = this.def();
    const layout = { ...this.layout() };
    const scopes: ScopeRef[][] = [[]];
    for (const { step, path } of allSteps(def)) step.Do?.forEach((_, i) => scopes.push([...path, { stepId: step.Id, branch: i }]));
    for (const path of scopes) {
      const list = scopeSteps(def, path) ?? [];
      if (list.some((s) => !layout[s.Id]) || (list.length && !layout[startKey(path)])) Object.assign(layout, arrange(list, path));
    }
    this.layout.set(layout);
  }

  private snapshotText(): string {
    return JSON.stringify({ def: this.def(), layout: this.layout() });
  }

  private restore(text: string): void {
    const snapshot = JSON.parse(text) as Snapshot;
    this.def.set(snapshot.def);
    this.layout.set(snapshot.layout);
    if (scopeSteps(snapshot.def, this.path()) === null) this.path.set([]);
    this.selection.set(null);
  }
}

/** Top-to-bottom layout of one scope, with its start marker above the first step. */
export function arrange(list: DslStep[], path: ScopeRef[]): Layout {
  const g = new Graph();
  g.setGraph({ rankdir: 'TB', nodesep: 40, ranksep: 56, marginx: 40, marginy: 40 });
  g.setDefaultEdgeLabel(() => ({}));
  g.setNode(START, { width: START_WIDTH, height: START_HEIGHT });
  for (const step of list) g.setNode(step.Id, { width: NODE_WIDTH, height: NODE_HEIGHT });
  if (list.length) g.setEdge(START, list[0].Id);
  const ids = new Set(list.map((s) => s.Id));
  for (const step of list) for (const e of edgesOf(step)) if (ids.has(e.to)) g.setEdge(step.Id, e.to);
  dagreLayout(g);

  const result: Layout = {};
  for (const step of list) {
    const n = g.node(step.Id);
    result[step.Id] = { x: Math.round(n.x! - NODE_WIDTH / 2), y: Math.round(n.y! - NODE_HEIGHT / 2) };
  }
  const s = g.node(START);
  result[startKey(path)] = { x: Math.round(s.x! - START_WIDTH / 2), y: Math.round(s.y! - START_HEIGHT / 2) };
  return result;
}

function startPositionFor(first: Position): Position {
  return { x: first.x + (NODE_WIDTH - START_WIDTH) / 2, y: first.y - 80 };
}

/** Fills in what older or hand-written definitions may leave out, so the editor can rely on it. */
function normalize(def: DslDefinition, id: string): DslDefinition {
  def.Id = id;
  def.Steps ??= [];
  return def;
}
