export interface CommandHistoryItem {
  id: string;
  usedAt: string;
}

const storageKey = (scope: string) => `slate.command-history.${scope}`;

export function readCommandHistory(scope: string): CommandHistoryItem[] {
  if (typeof localStorage === "undefined") return [];
  try {
    const value = JSON.parse(localStorage.getItem(storageKey(scope)) ?? "[]");
    return Array.isArray(value)
      ? value
          .filter((item) => typeof item?.id === "string" && typeof item?.usedAt === "string")
          .slice(0, 25)
      : [];
  } catch {
    return [];
  }
}

export function recordCommandUse(scope: string, id: string) {
  try {
    localStorage.setItem(
      storageKey(scope),
      JSON.stringify([
        { id, usedAt: new Date().toISOString() },
        ...readCommandHistory(scope).filter((item) => item.id !== id),
      ].slice(0, 25)),
    );
  } catch {}
}

export function clearCommandHistory(scope: string) {
  localStorage.removeItem(storageKey(scope));
}
