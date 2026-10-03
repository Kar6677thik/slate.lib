"use client";
import { useEffect } from "react";
import { useWorkspace } from "@/features/notes/workspace-context";
import { useCommandRegistry } from "@/features/commands/use-command-registry";
export function useShortcuts() {
  const w = useWorkspace();
  const { executeById } = useCommandRegistry();
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (!(e.ctrlKey || e.metaKey)) return;
      const editing = (e.target as HTMLElement)?.closest(".cm-editor");
      if (e.shiftKey && e.key.toLowerCase() === "p") {
        e.preventDefault();
        w.openCommandCenter(">");
        return;
      }
      if (e.key.toLowerCase() === "k" && !editing) {
        e.preventDefault();
        w.openCommandCenter();
        return;
      }
      if (e.key === "Tab" && w.tabs.length) {
        e.preventDefault();
        void executeById(e.shiftKey ? "workspace.previous-tab" : "workspace.next-tab");
        return;
      }
      if (e.key.toLowerCase() === "w" && w.active) {
        e.preventDefault();
        void executeById("workspace.close-tab");
        return;
      }
      if (e.key.toLowerCase() === "s") {
        e.preventDefault();
        void executeById("document.save");
        return;
      }
      if (e.key.toLowerCase() === "n") {
        e.preventDefault();
        void executeById("create.note");
        return;
      }
      if (e.shiftKey && e.key.toLowerCase() === "c") {
        e.preventDefault();
        void executeById("create.capture");
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [executeById, w]);
}
