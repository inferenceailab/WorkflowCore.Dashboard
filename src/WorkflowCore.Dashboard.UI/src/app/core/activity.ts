import { ActivityEntry } from './models';

/**
 * The server re-sends an entry with the same sequence when it adds details (e.g. a stack trace).
 * Replaces the existing entry in that case, otherwise prepends. Returns the new list and whether the entry was new.
 */
export function upsertActivity(list: ActivityEntry[], entry: ActivityEntry, limit = Infinity): [ActivityEntry[], boolean] {
  const index = list.findIndex((e) => e.sequence === entry.sequence);
  if (index >= 0) {
    const copy = [...list];
    copy[index] = entry;
    return [copy, false];
  }
  return [[entry, ...list].slice(0, limit), true];
}
