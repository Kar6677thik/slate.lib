// Notes the user just created should open ready for typing, regardless of the preferred reading mode.
const pending = new Set<string>();

export function requestEditing(noteId: string) {
  pending.add(noteId);
}

/** True once for a note that was opened to be written in; the intent is consumed by the first reader. */
export function takeEditingRequest(noteId: string) {
  return pending.delete(noteId);
}
