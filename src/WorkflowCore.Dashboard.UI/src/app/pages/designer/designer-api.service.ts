import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom, shareReplay } from 'rxjs';
import { dashboardUrl } from '../../core/api.service';
import { DslDefinition, Layout } from './dsl';

// Mirrors src/WorkflowCore.Dashboard.Designer/DesignerModels.cs

export interface CatalogProperty {
  name: string;
  type: string;
  kind: 'string' | 'bool' | 'number' | 'timespan' | 'datetime' | 'collection' | 'object';
}

export interface StepTypeInfo {
  type: string;
  name: string;
  category: string;
  description: string | null;
  isContainer: boolean;
  multipleBranches: boolean;
  inputs: CatalogProperty[];
  outputs: CatalogProperty[];
}

export interface DataTypeInfo {
  type: string;
  name: string;
  namespace: string | null;
  properties: CatalogProperty[];
}

export interface StepCatalog {
  steps: StepTypeInfo[];
  dataTypes: DataTypeInfo[];
}

export interface DesignerDraft {
  source: DslDefinition;
  layout: Layout | null;
  savedAt: string;
}

export interface DesignerVersion {
  version: number;
  source: DslDefinition;
  layout: Layout | null;
  publishedAt: string;
}

export interface DesignerDocument {
  id: string;
  draft: DesignerDraft | null;
  versions: DesignerVersion[];
  updatedAt: string;
}

export interface DesignerSummary {
  id: string;
  description: string | null;
  hasDraft: boolean;
  latestVersion: number | null;
  updatedAt: string;
}

export interface ValidationIssue {
  severity: 'error' | 'warning';
  message: string;
  stepId: string | null;
}

export interface ValidationResult {
  valid: boolean;
  issues: ValidationIssue[];
}

export interface PublishResponse {
  version: number;
  validation: ValidationResult;
}

@Injectable({ providedIn: 'root' })
export class DesignerApi {
  private readonly http = inject(HttpClient);
  private readonly base = dashboardUrl('api/designer/');

  // The catalog is built once per app start on the server; cache it for the session.
  private readonly catalog$ = this.http.get<StepCatalog>(this.base + 'catalog').pipe(shareReplay(1));

  catalog(): Promise<StepCatalog> {
    return firstValueFrom(this.catalog$);
  }

  list(): Promise<DesignerSummary[]> {
    return firstValueFrom(this.http.get<DesignerSummary[]>(this.base + 'definitions'));
  }

  get(id: string): Promise<DesignerDocument> {
    return firstValueFrom(this.http.get<DesignerDocument>(this.base + `definitions/${encodeURIComponent(id)}`));
  }

  saveDraft(id: string, source: DslDefinition, layout: Layout): Promise<DesignerDocument> {
    return firstValueFrom(this.http.put<DesignerDocument>(this.base + `definitions/${encodeURIComponent(id)}/draft`, { source, layout }));
  }

  discardDraft(id: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(this.base + `definitions/${encodeURIComponent(id)}/draft`));
  }

  validate(source: DslDefinition): Promise<ValidationResult> {
    return firstValueFrom(this.http.post<ValidationResult>(this.base + 'validate', { source }));
  }

  publish(id: string, source: DslDefinition, layout: Layout): Promise<PublishResponse> {
    return firstValueFrom(this.http.post<PublishResponse>(this.base + `definitions/${encodeURIComponent(id)}/publish`, { source, layout }));
  }

  import(text: string): Promise<{ source: DslDefinition }> {
    return firstValueFrom(this.http.post<{ source: DslDefinition }>(this.base + 'import', { text }));
  }
}
