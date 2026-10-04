import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { MatIconButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { MatTooltip } from '@angular/material/tooltip';
import { stepIcon } from '../../shared/workflow-graph/graph-model';
import { NODE_HEIGHT, NODE_WIDTH, START_HEIGHT, START_WIDTH, DesignerState } from './designer-state';
import { DslStep, Position, START, edgesOf, startKey, unreachable } from './dsl';

export const STEP_MIME = 'application/x-wfc-step';

interface CanvasNode {
  id: string;
  step: DslStep;
  x: number;
  y: number;
  title: string;
  subtitle: string;
  icon: string;
  container: boolean;
  branches: number;
  unreachable: boolean;
  issue: 'error' | 'warning' | null;
}

interface CanvasEdge {
  key: string;
  from: string;
  to: string;
  condition: string | null;
  path: string;
  label: string | null;
  labelAt: Position;
}

type Drag =
  | { kind: 'pan'; pointerId: number; startX: number; startY: number; origin: View; moved: boolean }
  | { kind: 'node'; pointerId: number; startX: number; startY: number; id: string; origin: Position; moved: boolean }
  | { kind: 'link'; pointerId: number; from: string; start: Position; current: Position };

interface View {
  x: number;
  y: number;
  k: number;
}

const GRID = 8;

export function shortTypeName(stepType: string): string {
  const name = stepType.split(',')[0];
  return name.slice(name.lastIndexOf('.') + 1);
}

/** Vertical S-curve from a node's bottom port to another node's top. */
function curve(from: Position, to: Position): string {
  const dy = Math.max(40, Math.abs(to.y - from.y) / 2);
  return `M${from.x},${from.y} C${from.x},${from.y + dy} ${to.x},${to.y - dy} ${to.x},${to.y}`;
}

function curveMidpoint(from: Position, to: Position): Position {
  const dy = Math.max(40, Math.abs(to.y - from.y) / 2);
  // Cubic Bézier at t = 0.5.
  return {
    x: (from.x + 3 * from.x + 3 * to.x + to.x) / 8,
    y: (from.y + 3 * (from.y + dy) + 3 * (to.y - dy) + to.y) / 8,
  };
}

@Component({
  selector: 'wfc-designer-canvas',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconButton, MatIcon, MatTooltip],
  templateUrl: './designer-canvas.html',
  styleUrl: './designer-canvas.scss',
})
export class DesignerCanvas {
  protected readonly state = inject(DesignerState);
  private readonly viewport = viewChild.required<ElementRef<HTMLDivElement>>('viewport');

  protected readonly view = signal<View>({ x: 0, y: 0, k: 1 });
  protected readonly drag = signal<Drag | null>(null);
  protected readonly transform = computed(() => `translate(${this.view().x},${this.view().y}) scale(${this.view().k})`);
  protected readonly zoomLabel = computed(() => `${Math.round(this.view().k * 100)}%`);

  protected readonly start = computed(() => {
    const pos = this.state.layout()[startKey(this.state.path())];
    return pos ? { x: pos.x, y: pos.y } : null;
  });

  protected readonly nodes = computed<CanvasNode[]>(() => {
    const steps = this.state.steps();
    const layout = this.state.layout();
    const orphaned = unreachable(steps);
    const issues = this.state.issuesByStep();
    return steps.map((step, i) => {
      const type = this.state.typeOf(step);
      const pos = layout[step.Id] ?? { x: 40, y: 40 + i * 100 };
      const stepIssues = issues.get(step.Id) ?? [];
      return {
        id: step.Id,
        step,
        x: pos.x,
        y: pos.y,
        title: step.Name || step.Id,
        subtitle: `${type?.name ?? shortTypeName(step.StepType)} · ${step.Id}`,
        icon: stepIcon(shortTypeName(step.StepType)),
        container: type?.isContainer ?? (step.Do?.length ?? 0) > 0,
        branches: step.Do?.length ?? 0,
        unreachable: orphaned.has(step.Id),
        issue: stepIssues.some((x) => x.severity === 'error') ? 'error' : stepIssues.length ? 'warning' : null,
      };
    });
  });

  protected readonly edges = computed<CanvasEdge[]>(() => {
    const nodes = new Map(this.nodes().map((n) => [n.id, n]));
    const bottom = (n: { x: number; y: number }, w: number, h: number) => ({ x: n.x + w / 2, y: n.y + h });
    const top = (n: CanvasNode) => ({ x: n.x + NODE_WIDTH / 2, y: n.y });
    const result: CanvasEdge[] = [];

    const start = this.start();
    const first = this.nodes()[0];
    if (start && first) {
      const from = bottom(start, START_WIDTH, START_HEIGHT);
      result.push({ key: `${START}>${first.id}`, from: START, to: first.id, condition: null, path: curve(from, top(first)), label: null, labelAt: from });
    }

    for (const node of this.nodes()) {
      for (const e of edgesOf(node.step)) {
        const target = nodes.get(e.to);
        if (!target) continue;
        const from = bottom(node, NODE_WIDTH, NODE_HEIGHT);
        const to = top(target);
        const label = e.condition === null ? null : e.condition.length > 26 ? `${e.condition.slice(0, 25)}…` : e.condition;
        result.push({ key: `${e.from}>${e.to}`, ...e, path: curve(from, to), label, labelAt: curveMidpoint(from, to) });
      }
    }
    return result;
  });

  protected readonly linkPreview = computed(() => {
    const d = this.drag();
    return d?.kind === 'link' ? curve(d.start, d.current) : null;
  });

  constructor() {
    afterNextRender(() => this.fit());
    // Entering or leaving a container branch shows a different set of steps.
    effect(() => {
      this.state.path();
      untracked(() => setTimeout(() => this.fit()));
    });
  }

  protected isSelected(id: string): boolean {
    const sel = this.state.selection();
    return sel?.kind === 'step' && sel.id === id;
  }

  protected isEdgeSelected(edge: CanvasEdge): boolean {
    const sel = this.state.selection();
    return sel?.kind === 'edge' && sel.from === edge.from && sel.to === edge.to;
  }

  protected clip(text: string, max: number): string {
    return text.length > max ? `${text.slice(0, max - 1)}…` : text;
  }

  // ---- pointer handling ----------------------------------------------------------------------

  protected onBackgroundDown(event: PointerEvent): void {
    if (event.button !== 0) return;
    this.viewport().nativeElement.focus();
    this.drag.set({ kind: 'pan', pointerId: event.pointerId, startX: event.clientX, startY: event.clientY, origin: this.view(), moved: false });
  }

  protected onNodeDown(node: CanvasNode, event: PointerEvent): void {
    if (event.button !== 0) return;
    event.stopPropagation();
    this.viewport().nativeElement.focus();
    this.state.selection.set({ kind: 'step', id: node.id });
    this.drag.set({ kind: 'node', pointerId: event.pointerId, startX: event.clientX, startY: event.clientY, id: node.id, origin: { x: node.x, y: node.y }, moved: false });
  }

  protected onPortDown(from: string, anchor: Position, event: PointerEvent): void {
    if (event.button !== 0) return;
    event.stopPropagation();
    (event.currentTarget as SVGElement).ownerSVGElement?.setPointerCapture(event.pointerId);
    this.drag.set({ kind: 'link', pointerId: event.pointerId, from, start: anchor, current: anchor });
  }

  protected onEdgeDown(edge: CanvasEdge, event: PointerEvent): void {
    event.stopPropagation();
    if (edge.from === START) return;
    this.viewport().nativeElement.focus();
    this.state.selection.set({ kind: 'edge', from: edge.from, to: edge.to });
  }

  protected onPointerMove(event: PointerEvent): void {
    const d = this.drag();
    if (!d || d.pointerId !== event.pointerId) return;

    if (d.kind === 'link') {
      this.drag.set({ ...d, current: this.toWorld(event.clientX, event.clientY) });
      return;
    }

    const dx = event.clientX - d.startX;
    const dy = event.clientY - d.startY;
    if (!d.moved && Math.hypot(dx, dy) < 4) return;
    if (!d.moved) {
      (event.currentTarget as Element).setPointerCapture(event.pointerId);
      if (d.kind === 'node') this.state.beginGesture();
      this.drag.set({ ...d, moved: true });
    }

    if (d.kind === 'pan') {
      this.view.set({ ...d.origin, x: d.origin.x + dx, y: d.origin.y + dy });
    } else {
      const k = this.view().k;
      const snap = (v: number) => Math.round(v / GRID) * GRID;
      this.state.moveDuringGesture(d.id, { x: snap(d.origin.x + dx / k), y: snap(d.origin.y + dy / k) });
    }
  }

  protected onPointerUp(event: PointerEvent): void {
    const d = this.drag();
    if (!d || d.pointerId !== event.pointerId) return;
    this.drag.set(null);

    if (d.kind === 'link') {
      const target = document.elementFromPoint(event.clientX, event.clientY)?.closest('[data-node]');
      const to = target?.getAttribute('data-node');
      if (to) this.state.connect(d.from, to);
    } else if (d.kind === 'node') {
      if (d.moved) this.state.endGesture();
    } else if (!d.moved) {
      this.state.selection.set(null);
    }
  }

  protected onWheel(event: WheelEvent): void {
    event.preventDefault();
    const rect = this.viewport().nativeElement.getBoundingClientRect();
    if (event.ctrlKey || event.metaKey) {
      this.zoomAt(Math.exp(-event.deltaY * 0.0015), event.clientX - rect.left, event.clientY - rect.top);
    } else {
      // In the editor the canvas owns the wheel: scroll pans, like other diagram tools.
      const v = this.view();
      this.view.set({ ...v, x: v.x - event.deltaX, y: v.y - event.deltaY });
    }
  }

  protected onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'Delete' || event.key === 'Backspace') {
      event.preventDefault();
      this.state.deleteSelection();
    } else if (event.key === 'Escape') {
      this.drag.set(null);
      this.state.selection.set(null);
    }
  }

  // ---- toolbox drops -------------------------------------------------------------------------

  protected onDragOver(event: DragEvent): void {
    if (event.dataTransfer?.types.includes(STEP_MIME)) {
      event.preventDefault();
      event.dataTransfer.dropEffect = 'copy';
    }
  }

  protected onDrop(event: DragEvent): void {
    const type = event.dataTransfer?.getData(STEP_MIME);
    const info = type ? this.state.stepTypes().get(type) : undefined;
    if (!info) return;
    event.preventDefault();
    const at = this.toWorld(event.clientX, event.clientY);
    this.state.addStep(info, { x: Math.round(at.x - NODE_WIDTH / 2), y: Math.round(at.y - NODE_HEIGHT / 2) });
    this.viewport().nativeElement.focus();
  }

  /** Where a step added by clicking the toolbox goes: below the lowest step, or the middle of the view. */
  newStepPosition(): Position {
    const nodes = this.nodes();
    if (nodes.length) {
      const lowest = nodes.reduce((a, b) => (b.y > a.y ? b : a));
      return { x: lowest.x, y: lowest.y + NODE_HEIGHT + 64 };
    }
    const el = this.viewport().nativeElement;
    const c = this.toWorld(el.getBoundingClientRect().left + el.clientWidth / 2, el.getBoundingClientRect().top + 140);
    return { x: Math.round(c.x - NODE_WIDTH / 2), y: Math.round(c.y) };
  }

  // ---- view ----------------------------------------------------------------------------------

  protected zoomBy(factor: number): void {
    const el = this.viewport().nativeElement;
    this.zoomAt(factor, el.clientWidth / 2, el.clientHeight / 2);
  }

  fit(): void {
    const el = this.viewport().nativeElement;
    if (!el.clientWidth) return;
    const boxes = this.nodes().map((n) => ({ x: n.x, y: n.y, r: n.x + NODE_WIDTH, b: n.y + NODE_HEIGHT }));
    const s = this.start();
    if (s) boxes.push({ x: s.x, y: s.y, r: s.x + START_WIDTH, b: s.y + START_HEIGHT });
    if (!boxes.length) {
      this.view.set({ x: el.clientWidth / 2 - NODE_WIDTH / 2, y: 80, k: 1 });
      return;
    }
    const minX = Math.min(...boxes.map((b) => b.x));
    const minY = Math.min(...boxes.map((b) => b.y));
    const width = Math.max(...boxes.map((b) => b.r)) - minX;
    const height = Math.max(...boxes.map((b) => b.b)) - minY;
    const k = Math.max(0.4, Math.min(1, (el.clientWidth - 80) / width, (el.clientHeight - 80) / height));
    this.view.set({ k, x: (el.clientWidth - width * k) / 2 - minX * k, y: Math.max(40, (el.clientHeight - height * k) / 2) - minY * k });
  }

  private zoomAt(factor: number, cx: number, cy: number): void {
    const v = this.view();
    const k = Math.min(2, Math.max(0.25, v.k * factor));
    this.view.set({ k, x: cx - ((cx - v.x) * k) / v.k, y: cy - ((cy - v.y) * k) / v.k });
  }

  private toWorld(clientX: number, clientY: number): Position {
    const rect = this.viewport().nativeElement.getBoundingClientRect();
    const v = this.view();
    return { x: (clientX - rect.left - v.x) / v.k, y: (clientY - rect.top - v.y) / v.k };
  }

  protected readonly NODE_WIDTH = NODE_WIDTH;
  protected readonly NODE_HEIGHT = NODE_HEIGHT;
  protected readonly START_WIDTH = START_WIDTH;
  protected readonly START_HEIGHT = START_HEIGHT;
  protected readonly START = START;
}
