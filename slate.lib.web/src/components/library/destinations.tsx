"use client";
import { useState } from "react";
import { FileText, Clock3, Plus } from "lucide-react";
import { formatDistanceToNow } from "date-fns";
import { useWorkspace } from "@/features/notes/workspace-context";
import { readRecent } from "@/lib/storage/recent";
import { FolderChildren } from "./explorer";
import { Button } from "@/components/ui/button";
import { Empty } from "@/components/common/primitives";
export function DestinationPage() {
  const w = useWorkspace();
  const [recent] = useState(() => readRecent(w.scope));
  if (w.nav === "recent")
    return (
      <>
        <div className="view-header">
          <p className="eyebrow">BROWSING HISTORY</p>
          <h1>Recent</h1>
          <p>Your last 50 opened notes, on this browser.</p>
        </div>
        <div className="scroll-area view-body">
          {recent.length ? (
            recent.map((r) => (
              <button
                className="result-row"
                key={r.id}
                onClick={() => w.open(r.id)}
              >
                <FileText size={18} />
                <div>
                  <strong>{r.title}</strong>
                  <span className="result-path">{r.path}</span>
                </div>
                <small>
                  {formatDistanceToNow(new Date(r.openedAt), {
                    addSuffix: true,
                  })}
                </small>
              </button>
            ))
          ) : (
            <Empty
              title="No recent notes"
              detail="Notes you open will appear here."
            />
          )}
        </div>
      </>
    );
  const inbox = w.nav === "inbox";
  return (
    <>
      <div className="view-header">
        <p className="eyebrow">{inbox ? "CAPTURE & ORGANIZE" : "WORKSPACE"}</p>
        <div className="section-heading">
          <h1>{inbox ? "Inbox" : "Library"}</h1>
          <Button
            variant="outline"
            size="sm"
            onClick={() =>
              inbox
                ? w.setCaptureOpen(true)
                : w.setOperation({ kind: "create-note", folder: "" })
            }
          >
            <Plus size={15} />
            {inbox ? "Capture" : "New note"}
          </Button>
        </div>
        <p>
          {inbox
            ? "Capture now. Organize when you’re ready."
            : "Browse folders or search your notes."}
        </p>
      </div>
      <div className="view-body scroll-area">
        <div className="section-heading">
          <h2>{inbox ? "Unsorted thoughts" : "Browse your library"}</h2>
          <Clock3 size={14} className="muted" />
        </div>
        <div
          className="library-browser"
          role="tree"
          aria-label={inbox ? "Inbox notes" : "Browse library"}
        >
          <FolderChildren key={w.nav} path={inbox ? "inbox" : ""} />
        </div>
      </div>
    </>
  );
}
