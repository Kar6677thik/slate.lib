"use client";
import { HistoryPanel } from "@/components/history/history-panel";
import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link, Info, History, ArrowUpRight } from "lucide-react";
import { useApi } from "@/lib/auth/context";
import { useWorkspace } from "@/features/notes/workspace-context";
import {
  IconButton,
  Loading,
  ErrorMessage,
  Empty,
} from "@/components/common/primitives";
export function DetailsRail() {
  const api = useApi(),
    w = useWorkspace();
  const [tab, setTab] = useState<"links" | "info" | "history">("links");
  const note = useQuery({
    queryKey: ["note", w.active],
    queryFn: ({ signal }) => api.note(w.active!, signal),
    enabled: !!w.active,
  });
  const links = useQuery({
    queryKey: ["links", w.active],
    queryFn: ({ signal }) => api.links(w.active!, signal),
    enabled: !!w.active,
  });
  return (
    <>
      <div className="rail-tabs">
        {(
          [
            ["links", Link, "Links"],
            ["info", Info, "Info"],
            ["history", History, "History"],
          ] as const
        ).map(([key, Icon, label]) => (
          <IconButton
            key={key}
            label={label}
            className={key === tab ? "active" : ""}
            onClick={() => setTab(key)}
          >
            <Icon size={17} />
          </IconButton>
        ))}
      </div>
      {!w.active ? (
        <Empty
          title="Document details"
          detail="Open a note to see its links and information."
        />
      ) : tab === "links" ? (
        <div className="rail-content">
          {links.isPending ? (
            <Loading />
          ) : links.error ? (
            <ErrorMessage error={links.error} />
          ) : (
            <>
              <h3>
                Backlinks <span>{links.data.backlinks.length}</span>
              </h3>
              {links.data.backlinks.length ? (
                links.data.backlinks.map((l) => (
                  <button
                    className="rail-link"
                    key={l.sourceId}
                    onClick={() => w.open(l.sourceId)}
                  >
                    {l.sourceTitle}
                    <ArrowUpRight size={13} />
                    <small>{l.sourcePath}</small>
                  </button>
                ))
              ) : (
                <p className="rail-empty">No notes link here yet.</p>
              )}
              <h3>
                Outgoing links <span>{links.data.outgoing.length}</span>
              </h3>
              {links.data.outgoing.length ? (
                links.data.outgoing.map((l, i) => (
                  <button
                    className="rail-link"
                    key={i}
                    disabled={!l.targetId}
                    onClick={() => l.targetId && w.open(l.targetId)}
                  >
                    {l.targetTitle ?? l.target}
                    <small>
                      {l.state === "resolved" ? l.targetPath : l.state}
                    </small>
                  </button>
                ))
              ) : (
                <p className="rail-empty">No outgoing links.</p>
              )}
            </>
          )}
        </div>
      ) : tab === "info" ? (
        <div className="rail-content">
          <h3>Document information</h3>
          {note.data && (
            <dl className="metadata">
              <dt>Title</dt>
              <dd>{note.data.title}</dd>
              <dt>Location</dt>
              <dd>{note.data.path}</dd>
              <dt>Format</dt>
              <dd>Markdown</dd>
              <dt>Identity</dt>
              <dd>{note.data.id}</dd>
              <dt>Size</dt>
              <dd>
                {new Blob([note.data.markdown]).size.toLocaleString()} bytes
              </dd>
            </dl>
          )}
        </div>
      ) : (
        <HistoryPanel id={w.active} />
      )}
    </>
  );
}
