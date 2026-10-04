// Mirrors src/WorkflowCore.Dashboard/Api/Dtos.cs

export type WorkflowStatus = 'Runnable' | 'Suspended' | 'Complete' | 'Terminated';

export const WORKFLOW_STATUSES: WorkflowStatus[] = ['Runnable', 'Suspended', 'Complete', 'Terminated'];

export type PointerStatus =
  | 'Legacy'
  | 'Pending'
  | 'Running'
  | 'Complete'
  | 'Sleeping'
  | 'WaitingForEvent'
  | 'Failed'
  | 'Compensated'
  | 'Cancelled'
  | 'PendingPredecessor';

export type Json = null | boolean | number | string | Json[] | { [key: string]: Json };

export interface JournalInfo {
  name: string;
  /** True when activity history survives restarts. */
  persistent: boolean;
  retentionDays: number | null;
  stepEvents: boolean;
}

export interface DashboardConfig {
  title: string;
  allowActions: boolean;
  persistenceProvider: string;
  startedAt: string;
  journal: JournalInfo;
  /** Add-ons installed on the server, e.g. "designer". */
  features: string[];
  /** The signed-in user, when the dashboard sits behind sign-in. */
  user: DashboardUser | null;
  /** Sign-out link, relative to the dashboard or absolute; null hides it. */
  signOutPath: string | null;
}

export interface DashboardUser {
  name: string;
  email: string | null;
  /** 'admin', 'viewer', or null when the app does not assign dashboard roles. */
  role: string | null;
}

export interface DefinitionSummary {
  id: string;
  version: number;
  description: string | null;
  dataType: string | null;
  stepCount: number;
  isLatest: boolean;
}

export interface OutcomeDto {
  nextStep: number;
  label: string | null;
  externalNextStepId: string | null;
}

export interface StepDto {
  id: number;
  externalId: string | null;
  name: string;
  stepType: string;
  stepTypeFullName: string;
  children: number[];
  outcomes: OutcomeDto[];
  errorBehavior: string | null;
  retryInterval: string | null;
  compensationStepId: number | null;
}

export interface DefinitionDetail {
  id: string;
  version: number;
  description: string | null;
  dataType: string | null;
  defaultErrorBehavior: string;
  defaultErrorRetryInterval: string | null;
  dataTemplate: Json;
  steps: StepDto[];
}

export interface InstanceSummary {
  id: string;
  definitionId: string;
  version: number;
  description: string | null;
  reference: string | null;
  status: WorkflowStatus;
  createTime: string;
  completeTime: string | null;
  nextExecution: string | null;
  activePointers: number;
  failedPointers: number;
  currentStep: string | null;
}

export interface PointerDto {
  id: string;
  stepId: number;
  stepName: string;
  stepType: string | null;
  status: PointerStatus;
  active: boolean;
  startTime: string | null;
  endTime: string | null;
  sleepUntil: string | null;
  retryCount: number;
  eventName: string | null;
  eventKey: string | null;
  eventPublished: boolean;
  predecessorId: string | null;
  children: string[];
  scope: string[];
  outcome: Json;
  eventData: Json;
  persistenceData: Json;
  contextItem: Json;
}

export interface InstanceDetail {
  summary: InstanceSummary;
  data: Json;
  executionPointers: PointerDto[];
}

export interface Page<T> {
  items: T[];
  skip: number;
  take: number;
  hasMore: boolean;
  /** Number of matches; null when the source cannot count (provider listing). */
  total: number | null;
  /** "journal": newest first, from the dashboard's index. "provider": Workflow Core's storage order. */
  source: 'journal' | 'provider';
}

export interface InstanceQuery {
  status?: WorkflowStatus | null;
  definitionId?: string | null;
  createdFrom?: string | null;
  createdTo?: string | null;
  skip: number;
  take: number;
}

export type ActivityType =
  | 'WorkflowStarted'
  | 'WorkflowCompleted'
  | 'WorkflowTerminated'
  | 'WorkflowSuspended'
  | 'WorkflowResumed'
  | 'WorkflowError'
  | 'StepStarted'
  | 'StepCompleted';

export interface ActivityEntry {
  /** Stable per event; an entry re-sent with the same ID (e.g. with its stack trace added) replaces the old one. */
  id: string;
  type: ActivityType;
  time: string;
  instanceId: string;
  definitionId: string;
  version: number;
  reference: string | null;
  executionPointerId: string | null;
  stepId: number | null;
  stepName: string | null;
  message: string | null;
  details: string | null;
}

export interface ActivityTotals {
  /** Start of the counted window; for the in-memory journal, when the app started. */
  since: string;
  counts: Record<string, number>;
}

export interface ApiError {
  code: string;
  message: string;
}
