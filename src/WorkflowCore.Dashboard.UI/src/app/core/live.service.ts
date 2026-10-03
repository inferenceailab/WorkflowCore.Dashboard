import { Injectable, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { Observable, Subject } from 'rxjs';
import { dashboardUrl } from './api.service';
import { ActivityEntry } from './models';

export type LiveState = 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

/** Live lifecycle events pushed by the server's DashboardHub. */
@Injectable({ providedIn: 'root' })
export class LiveService {
  readonly state = signal<LiveState>('disconnected');

  private readonly events = new Subject<ActivityEntry>();
  readonly activity$: Observable<ActivityEntry> = this.events.asObservable();

  private connection?: HubConnection;
  private retryTimer?: ReturnType<typeof setTimeout>;

  start(): void {
    if (this.connection) return;

    this.connection = new HubConnectionBuilder()
      .withUrl(dashboardUrl('hub'))
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    this.connection.on('activity', (entry: ActivityEntry) => this.events.next(entry));
    this.connection.onreconnecting(() => this.state.set('reconnecting'));
    this.connection.onreconnected(() => this.state.set('connected'));
    // Automatic reconnect gives up after a few attempts; keep trying at a slower pace.
    this.connection.onclose(() => {
      this.state.set('disconnected');
      this.scheduleRetry();
    });

    void this.connect();
  }

  private async connect(): Promise<void> {
    if (!this.connection) return;
    this.state.set('connecting');
    try {
      await this.connection.start();
      this.state.set('connected');
    } catch {
      this.state.set('disconnected');
      this.scheduleRetry();
    }
  }

  private scheduleRetry(): void {
    clearTimeout(this.retryTimer);
    this.retryTimer = setTimeout(() => void this.connect(), 10_000);
  }
}
