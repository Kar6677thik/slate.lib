import type { Note } from "@/lib/api/contracts";
export type Recent = Pick<Note, "id" | "path" | "title"> & { openedAt: string };
export function readRecent(scope: string): Recent[] {
  try {
    return JSON.parse(localStorage.getItem(`slate.recent.${scope}`) ?? "[]")
      .filter((r: Recent) => typeof r.id === "string")
      .slice(0, 50);
  } catch {
    return [];
  }
}
export function recordRecent(
  scope: string,
  note: Pick<Note, "id" | "path" | "title">,
) {
  try {
    localStorage.setItem(
      `slate.recent.${scope}`,
      JSON.stringify(
        [
          {
            id: note.id,
            path: note.path,
            title: note.title,
            openedAt: new Date().toISOString(),
          },
          ...readRecent(scope).filter((r) => r.id !== note.id),
        ].slice(0, 50),
      ),
    );
  } catch {}
}
