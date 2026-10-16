"use client";
import { useEffect, useRef } from "react";
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
  const list = useRef<HTMLDivElement>(null);
  useEffect(() => {
    list.current
      ?.querySelector('[aria-selected="true"]')
      ?.scrollIntoView({ block: "nearest", inline: "nearest" });
  }, [w.active]);
  return (
    <div className="document-tabs">
      <div
        className="tab-items"
        ref={list}
        onWheel={(event) => {
          // Let a vertical wheel scroll the strip sideways.
          if (event.deltaY && !event.deltaX)
            event.currentTarget.scrollLeft += event.deltaY;
        }}
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
        <div
          className="sr-only"
          role="tablist"
          aria-label="Open notes"
          aria-owns={w.tabs.map((tab) => `note-tab-${tab.id}`).join(" ")}
        />
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
              id={`note-tab-${t.id}`}
              aria-selected={w.active === t.id}
              title={t.path || t.title}
              onClick={() => w.open(t.id)}
            >
              <FileText size={14} />
              <span>{t.title}</span>
              {t.dirty && <span aria-label="Unsaved changes">•</span>}
            </button>
            <IconButton
              label={`Close ${t.title}`}
              onClick={() => w.close(t.id)}
            >
              <X size={13} />
            </IconButton>
          </div>
        ))}
      </div>
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
