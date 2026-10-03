"use client";
import { useQuery } from "@tanstack/react-query";
import { FileText, X } from "lucide-react";
import { useApi } from "@/lib/auth/context";
import { useWorkspace } from "@/features/notes/workspace-context";
import {
  ErrorMessage,
  Loading,
  IconButton,
} from "@/components/common/primitives";
import { NoteWorkbench } from "./note-workbench";
export function Tabs() {
  const w = useWorkspace();
  return (
    <div className="document-tabs">
      <div
        className="tab-items"
        role="tablist"
        aria-label="Open notes"
        onKeyDown={(event) => {
          if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key))
            return;
          const tabs = Array.from(
            event.currentTarget.querySelectorAll<HTMLButtonElement>(
              '[role="tab"]',
            ),
          );
          const index = tabs.indexOf(
            document.activeElement as HTMLButtonElement,
          );
          if (index < 0) return;
          event.preventDefault();
          const next =
            event.key === "Home"
              ? 0
              : event.key === "End"
                ? tabs.length - 1
                : (index +
                    (event.key === "ArrowRight" ? 1 : -1) +
                    tabs.length) %
                  tabs.length;
          tabs[next]?.focus();
          tabs[next]?.click();
        }}
      >
        {w.tabs.map((t) => (
          <div
            className={`document-tab ${w.active === t.id ? "active" : ""}`}
            key={t.id}
            onAuxClick={(e) => {
              if (e.button === 1) {
                e.preventDefault();
                w.close(t.id);
              }
            }}
          >
            <button
              role="tab"
              aria-selected={w.active === t.id}
              onClick={() => w.open(t.id)}
            >
              <FileText size={14} />
              <span>{t.title}</span>
              {t.dirty && <span aria-label="Unsaved changes">•</span>}
            </button>
          </div>
        ))}
      </div>
      {w.active && (
        <IconButton
          label={`Close ${w.tabs.find((t) => t.id === w.active)?.title ?? "note"}`}
          onClick={() => w.close(w.active!)}
        >
          <X size={14} />
        </IconButton>
      )}
    </div>
  );
}
export function Document({ id }: { id: string }) {
  const api = useApi();
  const q = useQuery({
    queryKey: ["note", id],
    queryFn: ({ signal }) => api.note(id, signal),
  });
  if (q.isPending) return <Loading label="Opening note…" />;
  if (q.error && !q.data)
    return <ErrorMessage error={q.error} retry={() => q.refetch()} />;
  return (
    <>
      {q.error && <ErrorMessage error={q.error} retry={() => q.refetch()} />}
      <NoteWorkbench note={q.data!} />
    </>
  );
}
