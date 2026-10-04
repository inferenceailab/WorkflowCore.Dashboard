import { Graph, layout } from '@dagrejs/dagre';
import { statusTone } from '../../core/format';
import { DefinitionDetail, PointerDto, StepDto } from '../../core/models';

export interface Point {
  x: number;
  y: number;
}

export interface GraphNode {
  id: string;
  kind: 'step' | 'exit';
  stepId: number;
  step: StepDto | null;
  x: number;
  y: number;
  width: number;
  height: number;
  icon: string;
  isStart: boolean;
}

/** The box drawn around a container step (If, ForEach, While, Parallel…) and its branches. */
export interface GraphCluster {
  id: string;
  stepId: number;
  x: number;
  y: number;
  width: number;
  height: number;
}

export type EdgeKind = 'flow' | 'branch' | 'return' | 'compensate';

export interface GraphEdge {
  id: string;
  kind: EdgeKind;
  /** "from->to" in step IDs, matched against pointer predecessors to highlight taken paths. */
  key: string;
  sourceStepId: number;
  targetStepId: number;
  label: string | null;
  points: Point[];
  labelPos: Point | null;
}

export interface GraphLayout {
  nodes: GraphNode[];
  clusters: GraphCluster[];
  edges: GraphEdge[];
  width: number;
  height: number;
}

export interface StepState {
  status: string;
  tone: string;
  runs: number;
}

export interface GraphOverlay {
  steps: Map<number, StepState>;
  takenEdges: Set<string>;
}

const NODE_WIDTH = 220;
const NODE_HEIGHT = 56;
const EXIT_SIZE = 14;
const CLUSTER_PADDING = 12;

const ICONS: Record<string, string> = {
  If: 'call_split',
  While: 'loop',
  Foreach: 'repeat',
  Sequence: 'account_tree',
  Decide: 'alt_route',
  OutcomeSwitch: 'alt_route',
  When: 'rule',
  WaitFor: 'hourglass_empty',
  Delay: 'timer',
  Schedule: 'schedule',
  Recur: 'update',
  SagaContainer: 'shield',
  Activity: 'person',
  SubWorkflowStepBody: 'subdirectory_arrow_right',
  InlineStepBody: 'code',
  ActionStepBody: 'code',
  EndStep: 'stop_circle',
};

export function stepIcon(stepType: string): string {
  return ICONS[stepType.split('<')[0]] ?? 'bolt';
}

/**
 * For every step inside a container's branches, the container that directly owns it.
 * Workflow Core lists only the first step of each branch as a child; the rest are reached by outcomes.
 */
export function containerOf(steps: StepDto[]): Map<number, number> {
  const byId = new Map(steps.map((s) => [s.id, s]));
  const parent = new Map<number, number>();
  for (const container of steps) {
    const queue = [...container.children];
    while (queue.length) {
      const id = queue.shift()!;
      if (parent.has(id)) continue;
      parent.set(id, container.id);
      for (const o of byId.get(id)?.outcomes ?? []) queue.push(o.nextStep);
    }
  }
  // Nested containers' children are reached through `children`, not outcomes, so each step's
  // parent is the nearest container that owns its branch.
  return parent;
}

export function layoutGraph(def: DefinitionDetail): GraphLayout {
  const steps = def.steps;
  const byId = new Map(steps.map((s) => [s.id, s]));
  const parent = containerOf(steps);
  const containers = steps.filter((s) => s.children.length > 0);

  const g = new Graph({ compound: true, multigraph: true });
  g.setGraph({ rankdir: 'TB', nodesep: 32, ranksep: 44, edgesep: 16, marginx: 24, marginy: 24 });
  g.setDefaultEdgeLabel(() => ({}));

  for (const c of containers) {
    g.setNode(`c${c.id}`, { width: 0, height: 0 });
    const owner = parent.get(c.id);
    if (owner !== undefined) g.setParent(`c${c.id}`, `c${owner}`);
  }

  for (const s of steps) {
    g.setNode(`s${s.id}`, { width: NODE_WIDTH, height: NODE_HEIGHT });
    // A container's own node sits inside its box, as the box's header.
    const box = s.children.length ? s.id : parent.get(s.id);
    if (box !== undefined) g.setParent(`s${s.id}`, `c${box}`);
  }

  const edges: Omit<GraphEdge, 'points' | 'labelPos'>[] = [];
  const addEdge = (from: string, to: string, edge: Omit<GraphEdge, 'points' | 'labelPos' | 'id'>) => {
    const id = `${from}>${to}:${edges.length}`;
    edges.push({ ...edge, id });
    const label = edge.label ? { width: edge.label.length * 6.5 + 12, height: 18, labelpos: 'c' as const } : {};
    g.setEdge(from, to, label, id);
  };

  for (const s of steps) {
    // Containers continue from an "exit" point at the bottom of their box, after the branches finish.
    const exitsFromBox = s.children.length > 0 && s.outcomes.length > 0;
    if (exitsFromBox) {
      g.setNode(`x${s.id}`, { width: EXIT_SIZE, height: EXIT_SIZE });
      g.setParent(`x${s.id}`, `c${s.id}`);
    }

    for (const childId of s.children) {
      if (byId.has(childId))
        addEdge(`s${s.id}`, `s${childId}`, { kind: 'branch', key: `${s.id}->${childId}`, sourceStepId: s.id, targetStepId: childId, label: null });
    }

    for (const o of s.outcomes) {
      if (!byId.has(o.nextStep)) continue;
      addEdge(exitsFromBox ? `x${s.id}` : `s${s.id}`, `s${o.nextStep}`, {
        kind: 'flow',
        key: `${s.id}->${o.nextStep}`,
        sourceStepId: s.id,
        targetStepId: o.nextStep,
        label: o.label,
      });
    }

    if (s.compensationStepId !== null && byId.has(s.compensationStepId)) {
      addEdge(`s${s.id}`, `s${s.compensationStepId}`, {
        kind: 'compensate',
        key: `${s.id}->${s.compensationStepId}`,
        sourceStepId: s.id,
        targetStepId: s.compensationStepId,
        label: 'compensate',
      });
    }
  }

  // Branch ends return to their container's exit point.
  for (const s of steps) {
    const owner = parent.get(s.id);
    if (owner === undefined || s.outcomes.length > 0) continue;
    const container = byId.get(owner)!;
    if (container.outcomes.length === 0) continue;
    addEdge(`s${s.id}`, `x${owner}`, { kind: 'return', key: `${s.id}->x${owner}`, sourceStepId: s.id, targetStepId: owner, label: null });
  }

  layout(g);

  const nodes: GraphNode[] = [];
  for (const s of steps) {
    const n = g.node(`s${s.id}`);
    nodes.push({
      id: `s${s.id}`,
      kind: 'step',
      stepId: s.id,
      step: s,
      x: n.x! - n.width / 2,
      y: n.y! - n.height / 2,
      width: n.width,
      height: n.height,
      icon: stepIcon(s.stepType),
      isStart: s.id === 0,
    });
    const exit = g.node(`x${s.id}`);
    if (exit) {
      nodes.push({
        id: `x${s.id}`,
        kind: 'exit',
        stepId: s.id,
        step: null,
        x: exit.x! - EXIT_SIZE / 2,
        y: exit.y! - EXIT_SIZE / 2,
        width: EXIT_SIZE,
        height: EXIT_SIZE,
        icon: '',
        isStart: false,
      });
    }
  }

  const clusters: GraphCluster[] = containers.map((c) => {
    const n = g.node(`c${c.id}`);
    return {
      id: `c${c.id}`,
      stepId: c.id,
      x: n.x! - n.width / 2 - CLUSTER_PADDING,
      y: n.y! - n.height / 2 - CLUSTER_PADDING,
      width: n.width + CLUSTER_PADDING * 2,
      height: n.height + CLUSTER_PADDING * 2,
    };
  });
  // Outer boxes first so inner ones paint on top.
  clusters.sort((a, b) => b.width * b.height - a.width * a.height);

  const laidOut: GraphEdge[] = edges.map((e) => {
    const [from, rest] = e.id.split('>');
    const to = rest.split(':')[0];
    const label = g.edge({ v: from, w: to, name: e.id });
    return {
      ...e,
      points: label?.points ?? [],
      labelPos: e.label && label?.x !== undefined ? { x: label.x, y: label.y! } : null,
    };
  });

  const graph = g.graph();
  return { nodes, clusters, edges: laidOut, width: graph.width ?? 0, height: graph.height ?? 0 };
}

/** Per-step execution state and the edges an instance actually took, from its execution pointers. */
export function buildOverlay(pointers: PointerDto[], workflowRunning: boolean): GraphOverlay {
  const stepOfPointer = new Map(pointers.map((p) => [p.id, p.stepId]));
  const byStep = new Map<number, PointerDto[]>();
  for (const p of pointers) byStep.set(p.stepId, [...(byStep.get(p.stepId) ?? []), p]);

  const steps = new Map<number, StepState>();
  for (const [stepId, list] of byStep) {
    // The step's state is its in-progress pointer if any, otherwise its latest one.
    const ordered = [...list].sort((a, b) => (b.startTime ?? '').localeCompare(a.startTime ?? ''));
    const current = (workflowRunning && ordered.find((p) => p.active || p.status === 'WaitingForEvent')) || ordered[0];
    const status = current.status === 'Failed' && current.active && current.sleepUntil ? 'Retrying' : current.status;
    steps.set(stepId, { status, tone: statusTone(status), runs: list.filter((p) => p.startTime).length });
  }

  const takenEdges = new Set<string>();
  for (const p of pointers) {
    const from = p.predecessorId ? stepOfPointer.get(p.predecessorId) : undefined;
    if (from !== undefined) takenEdges.add(`${from}->${p.stepId}`);
  }
  return { steps, takenEdges };
}

/** Smooth curve through dagre's edge points (Catmull-Rom converted to cubic Béziers). */
export function edgePath(points: Point[]): string {
  if (points.length === 0) return '';
  let d = `M${points[0].x},${points[0].y}`;
  for (let i = 0; i < points.length - 1; i++) {
    const p0 = points[i - 1] ?? points[i];
    const p1 = points[i];
    const p2 = points[i + 1];
    const p3 = points[i + 2] ?? p2;
    const c1 = { x: p1.x + (p2.x - p0.x) / 6, y: p1.y + (p2.y - p0.y) / 6 };
    const c2 = { x: p2.x - (p3.x - p1.x) / 6, y: p2.y - (p3.y - p1.y) / 6 };
    d += `C${c1.x},${c1.y} ${c2.x},${c2.y} ${p2.x},${p2.y}`;
  }
  return d;
}
