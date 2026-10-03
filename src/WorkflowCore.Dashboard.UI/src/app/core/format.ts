/** "WaitingForEvent" -> "Waiting for event" */
export function humanize(value: string): string {
  const spaced = value.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

/** Maps workflow, step and activity statuses onto the --wfc-* color tokens in styles.scss. */
export function statusTone(status: string): string {
  switch (status) {
    case 'Runnable':
    case 'Running':
    case 'WorkflowStarted':
    case 'StepStarted':
    case 'WorkflowResumed':
      return 'runnable';
    case 'Suspended':
    case 'Retrying':
    case 'Compensated':
    case 'WorkflowSuspended':
      return 'suspended';
    case 'Complete':
    case 'WorkflowCompleted':
    case 'StepCompleted':
      return 'complete';
    case 'Terminated':
    case 'Cancelled':
    case 'WorkflowTerminated':
      return 'terminated';
    case 'Failed':
    case 'WorkflowError':
      return 'failed';
    case 'WaitingForEvent':
      return 'waiting';
    case 'Sleeping':
      return 'sleeping';
    default:
      return 'pending';
  }
}

export function formatDuration(ms: number): string {
  if (ms < 0) return '—';
  if (ms < 1000) return `${Math.round(ms)} ms`;
  const s = ms / 1000;
  if (s < 60) return `${s.toFixed(s < 10 ? 2 : 1)} s`;
  const m = Math.floor(s / 60);
  if (m < 60) return `${m}m ${Math.round(s % 60)}s`;
  const h = Math.floor(m / 60);
  if (h < 24) return `${h}h ${m % 60}m`;
  return `${Math.floor(h / 24)}d ${h % 24}h`;
}

export function durationBetween(start: string | null, end: string | null, now = Date.now()): string {
  if (!start) return '—';
  const endMs = end ? Date.parse(end) : now;
  return formatDuration(endMs - Date.parse(start));
}

export function relativeTime(value: string | null, now = Date.now()): string {
  if (!value) return '—';
  const diff = Date.parse(value) - now;
  const abs = Math.abs(diff);
  const rtf = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });
  if (abs < 45_000) return diff <= 0 ? 'just now' : 'in a few seconds';
  if (abs < 3_600_000) return rtf.format(Math.round(diff / 60_000), 'minute');
  if (abs < 86_400_000) return rtf.format(Math.round(diff / 3_600_000), 'hour');
  return rtf.format(Math.round(diff / 86_400_000), 'day');
}

export function shortId(id: string): string {
  return id.length > 12 ? `${id.slice(0, 8)}…` : id;
}
