"use client";
import { useEffect } from "react";
import { useWorkspace } from "@/features/notes/workspace-context";
export function useShortcuts() {
  const w = useWorkspace();
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (!(e.ctrlKey || e.metaKey)) return;
      const editing = (e.target as HTMLElement)?.closest(".cm-editor");
      if (e.key.toLowerCase() === "k" && !editing) {
        e.preventDefault();
        w.setSearchOpen(true);
      }
      if (e.key === "Tab" && w.tabs.length) {
        e.preventDefault();
        const i = w.tabs.findIndex((t) => t.id === w.active);
        w.open(
          w.tabs[(i + (e.shiftKey ? -1 : 1) + w.tabs.length) % w.tabs.length]
            .id,
        );
      }
      if (e.key.toLowerCase() === "w" && w.active) {
        e.preventDefault();
        w.close(w.active);
      }
      if (e.shiftKey && e.key.toLowerCase() === "c") {
        e.preventDefault();
        w.setCaptureOpen(true);
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [w]);
}
