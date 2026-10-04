// Workflow Core DSL (DefinitionSourceV1) as edited by the designer, plus pure helpers over it.
// Property names match Workflow Core's JSON/YAML format exactly, so exports load as-is.

export type ErrorBehavior = 'Retry' | 'Suspend' | 'Terminate' | 'Compensate';
export const ERROR_BEHAVIORS: ErrorBehavior[] = ['Retry', 'Suspend', 'Terminate', 'Compensate'];

export interface DslStep {
  Id: string;
  StepType: string;
  Name?: string | null;
  NextStepId?: string | null;
  /** Target step ID -> condition. Every matching condition is followed. */
  SelectNextStep?: Record<string, string>;
  /** Step property -> expression (or a nested object for object inputs). */
  Inputs?: Record<string, unknown>;
  /** Data property -> expression over `step`. */
  Outputs?: Record<string, string>;
  /** Branches of a container step; each branch is its own list of steps. */
  Do?: DslStep[][];
  CompensateWith?: DslStep[];
  ErrorBehavior?: ErrorBehavior | null;
  RetryInterval?: string | null;
  CancelCondition?: string | null;
  ProceedOnCancel?: boolean;
  Saga?: boolean;
}

export interface DslDefinition {
  Id: string;
  Version?: number;
  Description?: string | null;
  DataType?: string | null;
  DefaultErrorBehavior?: ErrorBehavior;
  DefaultErrorRetryInterval?: string | null;
  Steps: DslStep[];
}

export interface Position {
  x: number;
  y: number;
}

/** Node positions by step ID, plus one start marker per scope (key `@start:<scope key>`). */
export type Layout = Record<string, Position>;

/** One level of nesting: branch `branch` of container step `stepId`. The root scope is the empty path. */
export interface ScopeRef {
  stepId: string;
  branch: number;
}

export interface ScopeEdge {
  from: string;
  to: string;
  /** null for the unconditional NextStepId connection. */
  condition: string | null;
}

export const START = '@start';

export function scopeKey(path: ScopeRef[]): string {
  return path.map((p) => `${p.stepId}#${p.branch}`).join('/') || 'root';
}

export function startKey(path: ScopeRef[]): string {
  return `${START}:${scopeKey(path)}`;
}

/** The list of steps a scope edits; null when the path no longer exists (e.g. its container was deleted). */
export function scopeSteps(def: DslDefinition, path: ScopeRef[]): DslStep[] | null {
  let list = def.Steps;
  for (const ref of path) {
    const container = list.find((s) => s.Id === ref.stepId);
    const branch = container?.Do?.[ref.branch];
    if (!branch) return null;
    list = branch;
  }
  return list;
}

/** Every step in the definition with the scope path that contains it. */
export function allSteps(def: DslDefinition): { step: DslStep; path: ScopeRef[] }[] {
  const result: { step: DslStep; path: ScopeRef[] }[] = [];
  const walk = (list: DslStep[], path: ScopeRef[]) => {
    for (const step of list) {
      result.push({ step, path });
      step.Do?.forEach((branch, i) => walk(branch, [...path, { stepId: step.Id, branch: i }]));
      if (step.CompensateWith?.length) walk(step.CompensateWith, path);
    }
  };
  walk(def.Steps, []);
  return result;
}

export function findStep(def: DslDefinition, id: string): DslStep | null {
  return allSteps(def).find((s) => s.step.Id === id)?.step ?? null;
}

export function edgesOf(step: DslStep): ScopeEdge[] {
  const edges: ScopeEdge[] = [];
  if (step.NextStepId) edges.push({ from: step.Id, to: step.NextStepId, condition: null });
  for (const [to, condition] of Object.entries(step.SelectNextStep ?? {})) edges.push({ from: step.Id, to, condition });
  return edges;
}

/** "Wait for event" becomes WaitForEvent1, WaitForEvent2, ... */
export function uniqueId(def: DslDefinition, name: string): string {
  const taken = new Set(allSteps(def).map((s) => s.step.Id));
  const clean =
    name
      .split(/[^A-Za-z0-9]+/)
      .filter(Boolean)
      .map((w) => w[0].toUpperCase() + w.slice(1))
      .join('')
      .replace(/^[0-9]+/, '') || 'Step';
  for (let i = 1; ; i++) {
    const id = `${clean}${i}`;
    if (!taken.has(id)) return id;
  }
}

export function connect(def: DslDefinition, path: ScopeRef[], from: string, to: string): boolean {
  const list = scopeSteps(def, path);
  if (!list || from === to) return false;
  if (from === START) {
    // The first step of a list is where it starts.
    const index = list.findIndex((s) => s.Id === to);
    if (index <= 0) return false;
    list.unshift(...list.splice(index, 1));
    return true;
  }
  const step = list.find((s) => s.Id === from);
  if (!step || step.NextStepId === to || step.SelectNextStep?.[to] !== undefined) return false;
  if (!step.NextStepId && !Object.keys(step.SelectNextStep ?? {}).length) {
    step.NextStepId = to;
  } else {
    // Further connections are conditional; "true" means always, until the user writes a condition.
    step.SelectNextStep = { ...(step.SelectNextStep ?? {}), [to]: 'true' };
  }
  return true;
}

export function disconnect(def: DslDefinition, from: string, to: string): void {
  const step = findStep(def, from);
  if (!step) return;
  if (step.NextStepId === to) step.NextStepId = null;
  if (step.SelectNextStep) delete step.SelectNextStep[to];
}

/** An empty condition makes the connection unconditional (NextStepId) when that slot is free. */
export function setCondition(def: DslDefinition, from: string, to: string, condition: string): void {
  const step = findStep(def, from);
  if (!step) return;
  const text = condition.trim();
  const select = { ...(step.SelectNextStep ?? {}) };
  if (step.NextStepId === to) {
    if (!text) return;
    step.NextStepId = null;
    select[to] = text;
  } else if (!text && !step.NextStepId) {
    delete select[to];
    step.NextStepId = to;
  } else {
    select[to] = text || 'true';
  }
  step.SelectNextStep = select;
}

/** Removes a step (and anything nested in it) and every connection to it. Returns the removed IDs. */
export function removeStep(def: DslDefinition, id: string): string[] {
  const entry = allSteps(def).find((s) => s.step.Id === id);
  if (!entry) return [];
  const list = scopeSteps(def, entry.path);
  if (!list) return [];
  list.splice(list.indexOf(entry.step), 1);

  const removed = [id];
  const collect = (step: DslStep) => {
    for (const child of [...(step.Do ?? []).flat(), ...(step.CompensateWith ?? [])]) {
      removed.push(child.Id);
      collect(child);
    }
  };
  collect(entry.step);

  for (const { step } of allSteps(def)) {
    if (step.NextStepId && removed.includes(step.NextStepId)) step.NextStepId = null;
    for (const target of removed) if (step.SelectNextStep) delete step.SelectNextStep[target];
  }
  return removed;
}

export function renameStep(def: DslDefinition, oldId: string, newId: string): boolean {
  if (!newId || oldId === newId || allSteps(def).some((s) => s.step.Id === newId)) return false;
  for (const { step } of allSteps(def)) {
    if (step.Id === oldId) step.Id = newId;
    if (step.NextStepId === oldId) step.NextStepId = newId;
    if (step.SelectNextStep && oldId in step.SelectNextStep) {
      step.SelectNextStep = Object.fromEntries(
        Object.entries(step.SelectNextStep).map(([k, v]) => [k === oldId ? newId : k, v]),
      );
    }
  }
  return true;
}

/** Steps nobody connects to (other than the first) never run. */
export function unreachable(list: DslStep[]): Set<string> {
  const seen = new Set<string>();
  const byId = new Map(list.map((s) => [s.Id, s]));
  const queue = list.length ? [list[0]] : [];
  while (queue.length) {
    const step = queue.shift()!;
    if (seen.has(step.Id)) continue;
    seen.add(step.Id);
    for (const e of edgesOf(step)) {
      const next = byId.get(e.to);
      if (next) queue.push(next);
    }
  }
  return new Set(list.filter((s) => !seen.has(s.Id)).map((s) => s.Id));
}

/** Readable JSON for export: drops empty collections and default flags that Workflow Core fills in itself. */
export function cleanForExport(def: DslDefinition): DslDefinition {
  const cleanStep = (step: DslStep): DslStep => {
    const out: Record<string, unknown> = {};
    for (const [key, value] of Object.entries(step)) {
      if (value === null || value === undefined || value === false) continue;
      if (Array.isArray(value) && value.length === 0) continue;
      if (typeof value === 'object' && !Array.isArray(value) && Object.keys(value as object).length === 0) continue;
      if (key === 'Do') out[key] = (value as DslStep[][]).map((branch) => branch.map(cleanStep));
      else if (key === 'CompensateWith') out[key] = (value as DslStep[]).map(cleanStep);
      else out[key] = value;
    }
    return out as unknown as DslStep;
  };
  const out: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(def)) {
    if (value === null || value === undefined) continue;
    out[key] = key === 'Steps' ? (value as DslStep[]).map(cleanStep) : value;
  }
  return out as unknown as DslDefinition;
}

export function clone<T>(value: T): T {
  return structuredClone(value);
}
