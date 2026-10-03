import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { MatIconButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { MatTooltip } from '@angular/material/tooltip';
import { Json } from '../core/models';

@Component({
  selector: 'wfc-json',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconButton, MatIcon, MatTooltip],
  template: `
    @if (text() === null) {
      <div class="empty">{{ emptyText() }}</div>
    } @else {
      <button mat-icon-button class="copy" (click)="copy()" [matTooltip]="copied() ? 'Copied' : 'Copy JSON'">
        <mat-icon>{{ copied() ? 'check' : 'content_copy' }}</mat-icon>
      </button>
      <pre>{{ text() }}</pre>
    }
  `,
  styles: `
    :host { display: block; position: relative; }
    pre {
      margin: 0;
      padding: 16px;
      overflow: auto;
      max-height: 560px;
      font-family: var(--wfc-mono);
      font-size: 12.5px;
      line-height: 1.55;
      white-space: pre;
    }
    .copy { position: absolute; top: 6px; right: 6px; }
  `,
})
export class JsonView {
  readonly value = input<Json | undefined>(undefined);
  readonly emptyText = input('No data');

  protected readonly copied = signal(false);
  protected readonly text = computed(() => {
    const v = this.value();
    return v === null || v === undefined ? null : JSON.stringify(v, null, 2);
  });

  protected async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.text() ?? '');
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 1500);
    } catch {
      // Clipboard can be blocked (insecure origin, permissions); nothing else to do.
    }
  }
}
