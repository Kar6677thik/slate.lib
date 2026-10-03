import { it, expect } from "vitest";
import { syncLabel } from "@/features/git/state";
it("does not claim synchronization for pending, local-only or unknown server state", () => {
  expect(syncLabel("Synced")).toBe("Synced");
  expect(syncLabel("Synced", true)).toBe("Changes pending");
  expect(syncLabel("LocalOnly")).toBe("Saved on server");
  expect(syncLabel("Diverged")).toBe("Needs attention");
  expect(syncLabel(undefined)).toBe("Needs attention");
});
