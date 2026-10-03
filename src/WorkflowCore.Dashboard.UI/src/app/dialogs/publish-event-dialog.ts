import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIcon } from '@angular/material/icon';
import { MatInput } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ApiService, errorMessage } from '../core/api.service';
import { Json } from '../core/models';

export interface PublishEventData {
  eventName?: string;
  eventKey?: string;
}

@Component({
  selector: 'wfc-publish-event-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatDialogModule, MatButton, MatFormFieldModule, MatInput, MatIcon],
  template: `
    <h2 mat-dialog-title>Publish event</h2>
    <mat-dialog-content>
      <p class="muted intro">Resumes workflows waiting on this event name and key (a WaitFor step).</p>
      <div class="row">
        <mat-form-field class="grow">
          <mat-label>Event name</mat-label>
          <input matInput [(ngModel)]="eventName" required placeholder="OrderShipped" />
        </mat-form-field>
        <mat-form-field class="grow">
          <mat-label>Event key</mat-label>
          <input matInput [(ngModel)]="eventKey" placeholder="order-1042" />
        </mat-form-field>
      </div>
      <mat-form-field class="full">
        <mat-label>Event data (JSON)</mat-label>
        <textarea matInput class="code" rows="6" [(ngModel)]="dataText" spellcheck="false"
          placeholder='"a string", 42, or { "trackingNumber": "1Z999" }'></textarea>
        <mat-hint>Optional. Any JSON value.</mat-hint>
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
      <button mat-flat-button (click)="publish()" [disabled]="!eventName().trim() || !!jsonError() || busy()">
        <mat-icon>send</mat-icon> Publish
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .intro { margin: 0 0 16px; }
    .row { display: flex; gap: 12px; }
    .grow { flex: 1; min-width: 0; }
    .full { width: 100%; }
    textarea.code { font-family: var(--wfc-mono); font-size: 12.5px; line-height: 1.5; }
    @media (max-width: 520px) { .row { flex-direction: column; gap: 0; } }
  `,
})
export class PublishEventDialog {
  private readonly api = inject(ApiService);
  private readonly ref = inject(MatDialogRef<PublishEventDialog, boolean>);
  private readonly snack = inject(MatSnackBar);
  private readonly input = inject<PublishEventData>(MAT_DIALOG_DATA, { optional: true }) ?? {};

  protected readonly eventName = signal(this.input.eventName ?? '');
  protected readonly eventKey = signal(this.input.eventKey ?? '');
  protected readonly dataText = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

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

  protected async publish(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const text = this.dataText().trim();
      const data = text ? (JSON.parse(text) as Json) : undefined;
      await this.api.publishEvent(this.eventName().trim(), this.eventKey().trim(), data, null);
      this.snack.open(`Published ${this.eventName().trim()}`, undefined, { duration: 3000 });
      this.ref.close(true);
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
