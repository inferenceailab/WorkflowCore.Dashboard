import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterNextRender,
  computed,
  effect,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { MatIconButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { MatTooltip } from '@angular/material/tooltip';
import { humanize } from '../../core/format';
import { DefinitionDetail, PointerDto } from '../../core/models';
import { GraphEdge, GraphNode, buildOverlay, edgePath, layoutGraph } from './graph-model';

interface View {
  x: number;
  y: number;
  k: number;
}

const MIN_ZOOM = 0.2;
const MAX_ZOOM = 2;
/** Fitting never zooms out below this for height alone; taller graphs are panned instead. */
const READABLE_ZOOM = 0.8;

/**
 * Read-only flowchart of a workflow definition. With execution pointers it also shows
 * each step's state, how often it ran, and which paths were taken.
 */
@Component({
  selector: 'wfc-workflow-graph',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconButton, MatIcon, MatTooltip],
  templateUrl: './workflow-graph.html',
  styleUrl: './workflow-graph.scss',
})
export class WorkflowGraph {
  readonly definition = input.required<DefinitionDetail>();
  /** Execution pointers of an instance; null shows the plain definition. */
  readonly pointers = input<PointerDto[] | null>(null);
  readonly workflowRunning = input(false);
  readonly selectedStepId = input<number | null>(null);
  readonly stepSelected = output<number | null>();

  private readonly viewport = viewChild.required<ElementRef<HTMLDivElement>>('viewport');

  protected readonly layout = computed(() => layoutGraph(this.definition()));
  protected readonly overlay = computed(() => {
    const pointers = this.pointers();
    return pointers ? buildOverlay(pointers, this.workflowRunning()) : null;
  });
  protected readonly view = signal<View>({ x: 0, y: 0, k: 1 });
  protected readonly transform = computed(() => {
    const v = this.view();
    return `translate(${v.x},${v.y}) scale(${v.k})`;
  });
  protected readonly zoomLabel = computed(() => `${Math.round(this.view().k * 100)}%`);
  /** The canvas grows with the graph, within limits, so short workflows do not leave empty space. */
  protected readonly viewportHeight = computed(() => Math.min(720, Math.max(320, this.layout().height + 32)));

  private drag: { pointerId: number; startX: number; startY: number; view: View; moved: boolean } | null = null;
  private rendered = false;

  constructor() {
    afterNextRender(() => {
      this.rendered = true;
      this.fit();
    });
    // Fit again when a different definition is shown; live updates of the same one keep the user's view.
    effect(() => {
      this.definition().id;
      this.definition().version;
      if (untracked(() => this.rendered)) untracked(() => this.fit());
    });
  }

  protected path(edge: GraphEdge): string {
    return edgePath(edge.points);
  }

  protected nodeState(node: GraphNode) {
    return this.overlay()?.steps.get(node.stepId) ?? null;
  }

  protected nodeTone(node: GraphNode): string {
    const overlay = this.overlay();
    if (!overlay) return 'idle';
    return overlay.steps.get(node.stepId)?.tone ?? 'unvisited';
  }

  protected edgeClass(edge: GraphEdge): string {
    const overlay = this.overlay();
    let state = '';
    if (overlay) {
      const taken =
        edge.kind === 'return'
          ? overlay.steps.get(edge.sourceStepId)?.status === 'Complete'
          : overlay.takenEdges.has(edge.key);
      state = taken ? ' taken' : ' untaken';
    }
    return `edge ${edge.kind}${state}`;
  }

  protected marker(edge: GraphEdge): string {
    if (edge.kind === 'return') return '';
    const cls = this.edgeClass(edge);
    if (cls.includes('taken') && !cls.includes('untaken')) return 'url(#wfc-arrow-taken)';
    return edge.kind === 'compensate' ? 'url(#wfc-arrow-compensate)' : 'url(#wfc-arrow)';
  }

  protected title(node: GraphNode): string {
    const step = node.step!;
    const state = this.nodeState(node);
    const lines = [`${step.name} (#${step.id})`, step.stepTypeFullName];
    if (state) lines.push(`${humanize(state.status)}${state.runs > 1 ? `, ran ${state.runs} times` : ''}`);
    else if (this.overlay()) lines.push('Not run');
    return lines.join('\n');
  }

  protected clip(text: string, max: number): string {
    return text.length > max ? `${text.slice(0, max - 1)}…` : text;
  }

  protected statusLabel(status: string): string {
    return humanize(status);
  }

  protected select(node: GraphNode, event: Event): void {
    event.stopPropagation();
    if (this.drag?.moved) return;
    this.stepSelected.emit(this.selectedStepId() === node.stepId ? null : node.stepId);
  }

  // Pan by dragging the background; Ctrl/⌘ + wheel zooms around the cursor. Plain wheel scrolls the page.
  protected onPointerDown(event: PointerEvent): void {
    if (event.button !== 0) return;
    this.drag = { pointerId: event.pointerId, startX: event.clientX, startY: event.clientY, view: this.view(), moved: false };
  }

  protected onPointerMove(event: PointerEvent): void {
    const drag = this.drag;
    if (!drag || drag.pointerId !== event.pointerId) return;
    const dx = event.clientX - drag.startX;
    const dy = event.clientY - drag.startY;
    if (!drag.moved && Math.hypot(dx, dy) < 4) return;
    if (!drag.moved) {
      drag.moved = true;
      (event.currentTarget as Element).setPointerCapture(event.pointerId);
    }
    this.view.set({ ...drag.view, x: drag.view.x + dx, y: drag.view.y + dy });
  }

  protected onPointerUp(event: PointerEvent): void {
    if (this.drag?.pointerId !== event.pointerId) return;
    // Cleared after the click event, so a drag that ends over a node does not select it.
    setTimeout(() => (this.drag = null));
  }

  /** Clicks on empty space clear the selection; node clicks stop propagation before reaching here. */
  protected onBackgroundClick(): void {
    if (!this.drag?.moved) this.stepSelected.emit(null);
  }

  protected onWheel(event: WheelEvent): void {
    if (!event.ctrlKey && !event.metaKey) return;
    event.preventDefault();
    const rect = this.viewport().nativeElement.getBoundingClientRect();
    this.zoomAt(Math.exp(-event.deltaY * 0.0015), event.clientX - rect.left, event.clientY - rect.top);
  }

  protected zoomBy(factor: number): void {
    const el = this.viewport().nativeElement;
    this.zoomAt(factor, el.clientWidth / 2, el.clientHeight / 2);
  }

  fit(): void {
    const el = this.viewport().nativeElement;
    const { width, height } = this.layout();
    if (!width || !height || !el.clientWidth) return;
    const byWidth = (el.clientWidth - 32) / width;
    const byHeight = (el.clientHeight - 32) / height;
    const k = Math.max(MIN_ZOOM, Math.min(1, byWidth, Math.max(byHeight, READABLE_ZOOM)));
    this.view.set({
      k,
      x: (el.clientWidth - width * k) / 2,
      // Centered when it fits; otherwise start at the top, where the workflow starts.
      y: Math.max(16, (el.clientHeight - height * k) / 2),
    });
  }

  private zoomAt(factor: number, cx: number, cy: number): void {
    const v = this.view();
    const k = Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, v.k * factor));
    // Keep the point under the cursor fixed.
    this.view.set({ k, x: cx - ((cx - v.x) * k) / v.k, y: cy - ((cy - v.y) * k) / v.k });
  }
}
