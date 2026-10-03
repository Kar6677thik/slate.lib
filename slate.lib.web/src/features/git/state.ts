export function syncLabel(state?: string, pending = false) {
  if (state === "Syncing") return "Syncing";
  if (pending) return "Changes pending";
  if (state === "Synced") return "Synced";
  if (state === "LocalOnly") return "Saved on server";
  if (state === "NotInitialized") return "History unavailable";
  return "Needs attention";
}
