/**
 * A one-shot request to show a specific comment thread when a note opens, used by the notifications page. The note
 * workbench takes it on mount, opens Read mode, and opens the passage drawer or scrolls to the discussion thread.
 */
const pending = new Map<string, string>();
const listeners = new Set<(noteId: string) => void>();

export function requestThreadFocus(noteId: string, threadId: string) {
  pending.set(noteId, threadId);
  listeners.forEach((listener) => listener(noteId));
}

export function takeThreadFocus(noteId: string) {
  const threadId = pending.get(noteId) ?? null;
  pending.delete(noteId);
  return threadId;
}

/** Notifies an already-open note that a thread was requested for it. */
export function onThreadFocus(listener: (noteId: string) => void) {
  listeners.add(listener);
  return () => void listeners.delete(listener);
}
