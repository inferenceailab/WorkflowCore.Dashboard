import { HttpClient, HttpErrorResponse, HttpInterceptorFn, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  ActivityEntry,
  ActivityTotals,
  ApiError,
  DashboardConfig,
  DefinitionDetail,
  DefinitionSummary,
  InstanceDetail,
  InstanceQuery,
  InstanceSummary,
  Json,
  Page,
} from './models';

/** Resolves a path against the dashboard root. The server rewrites <base href> to the mount prefix. */
export function dashboardUrl(path: string): string {
  return new URL(path, document.baseURI).toString();
}

/**
 * Marks requests as coming from the dashboard. The server rejects changes without this header, which stops
 * other websites from triggering them (they cannot add custom headers without CORS approval).
 */
export const dashboardHeaderInterceptor: HttpInterceptorFn = (req, next) =>
  req.url.startsWith(dashboardUrl('api/')) ? next(req.clone({ setHeaders: { 'X-Wfc-Dashboard': '1' } })) : next(req);

export function errorMessage(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    const body = err.error as Partial<ApiError> | null;
    if (body?.message) return body.message;
    if (err.status === 0) return 'Cannot reach the dashboard API.';
    if (err.status === 403) return 'Access to the dashboard was denied.';
    return `${err.status} ${err.statusText}`;
  }
  return err instanceof Error ? err.message : String(err);
}

export function errorCode(err: unknown): string | null {
  return err instanceof HttpErrorResponse ? ((err.error as Partial<ApiError> | null)?.code ?? null) : null;
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly base = dashboardUrl('api/');

  readonly config = signal<DashboardConfig>({
    title: 'Workflow Core',
    allowActions: false,
    persistenceProvider: '',
    startedAt: new Date().toISOString(),
    journal: { name: '', persistent: false, retentionDays: null, stepEvents: true },
    features: [],
  });

  hasFeature(name: string): boolean {
    return this.config().features.includes(name);
  }

  async loadConfig(): Promise<void> {
    try {
      this.config.set(await this.get<DashboardConfig>('config'));
    } catch {
      // Keep the read-only defaults; pages surface their own errors.
    }
  }

  definitions() {
    return this.get<DefinitionSummary[]>('definitions');
  }

  definition(id: string, version: number) {
    return this.get<DefinitionDetail>(`definitions/${encodeURIComponent(id)}/${version}`);
  }

  instances(query: InstanceQuery) {
    let params = new HttpParams().set('skip', query.skip).set('take', query.take);
    if (query.status) params = params.set('status', query.status);
    if (query.definitionId) params = params.set('definitionId', query.definitionId);
    if (query.createdFrom) params = params.set('createdFrom', query.createdFrom);
    if (query.createdTo) params = params.set('createdTo', query.createdTo);
    return this.get<Page<InstanceSummary>>('instances', params);
  }

  instance(id: string) {
    return this.get<InstanceDetail>(`instances/${encodeURIComponent(id)}`);
  }

  activity(options: { instanceId?: string; take?: number; before?: string; steps?: boolean } = {}) {
    let params = new HttpParams().set('take', options.take ?? 100);
    if (options.instanceId) params = params.set('instanceId', options.instanceId);
    if (options.before) params = params.set('before', options.before);
    if (options.steps === false) params = params.set('steps', 'false');
    return this.get<ActivityEntry[]>('activity', params);
  }

  activityTotals(hours: number) {
    return this.get<ActivityTotals>('activity/totals', new HttpParams().set('hours', hours));
  }

  startWorkflow(definitionId: string, version: number | null, data: Json | undefined, reference: string | null) {
    return this.post<{ id: string }>('instances', { definitionId, version, data, reference });
  }

  suspend(id: string) {
    return this.post<{ success: boolean }>(`instances/${encodeURIComponent(id)}/suspend`);
  }

  resume(id: string) {
    return this.post<{ success: boolean }>(`instances/${encodeURIComponent(id)}/resume`);
  }

  terminate(id: string) {
    return this.post<{ success: boolean }>(`instances/${encodeURIComponent(id)}/terminate`);
  }

  publishEvent(eventName: string, eventKey: string, eventData: Json | undefined, effectiveDate: string | null) {
    return this.post<{ success: boolean }>('events', { eventName, eventKey, eventData, effectiveDate });
  }

  private get<T>(path: string, params?: HttpParams): Promise<T> {
    return firstValueFrom(this.http.get<T>(this.base + path, { params }));
  }

  private post<T>(path: string, body: unknown = {}): Promise<T> {
    return firstValueFrom(this.http.post<T>(this.base + path, body));
  }
}
