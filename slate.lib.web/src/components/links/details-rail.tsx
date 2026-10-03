"use client";
import { HistoryPanel } from "@/components/history/history-panel";
import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  Link,
  Info,
  History,
  ArrowUpRight,
  Network,
  Waypoints,
} from "lucide-react";
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
  const [tab, setTab] = useState<
    "links" | "related" | "graph" | "info" | "history"
  >("links");
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
            ["related", Waypoints, "Related notes"],
            ["graph", Network, "Note graph"],
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
      ) : tab === "related" ? (
        <RelatedPanel id={w.active} />
      ) : tab === "graph" ? (
        <GraphPanel id={w.active} />
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

function RelatedPanel({ id }: { id: string }) {
  const api = useApi();
  const w = useWorkspace();
  const query = useQuery({
    queryKey: ["related", id],
    queryFn: ({ signal }) => api.related(id, signal),
  });
  return (
    <div className="rail-content">
      <h3>Related notes</h3>
      {query.isPending ? (
        <Loading label="Finding related notes…" />
      ) : query.error ? (
        <ErrorMessage error={query.error} retry={() => query.refetch()} />
      ) : query.data.length ? (
        query.data.map((note) => (
          <button
            className="rail-link related-note"
            key={note.id}
            onClick={() => w.open(note.id)}
          >
            {note.title}
            <ArrowUpRight size={13} />
            <small>{note.path}</small>
            <span>{note.reasons.join(" · ")}</span>
          </button>
        ))
      ) : (
        <p className="rail-empty">
          No related notes meet the server’s deterministic ranking rules.
        </p>
      )}
    </div>
  );
}

function GraphPanel({ id }: { id: string }) {
  const api = useApi();
  const w = useWorkspace();
  const [depth, setDepth] = useState(1);
  const query = useQuery({
    queryKey: ["graph", id, depth],
    queryFn: ({ signal }) => api.graph(id, { depth, limit: 40 }, signal),
  });
  return (
    <div className="rail-content">
      <div className="rail-section-heading">
        <h3>Current note graph</h3>
        <label>
          Depth
          <select
            value={depth}
            onChange={(event) => setDepth(Number(event.target.value))}
          >
            <option value={1}>1</option>
            <option value={2}>2</option>
            <option value={3}>3</option>
          </select>
        </label>
      </div>
      {query.isPending ? (
        <Loading label="Building bounded graph…" />
      ) : query.error ? (
        <ErrorMessage error={query.error} retry={() => query.refetch()} />
      ) : (
        <>
          <p className="rail-summary">
            {query.data.nodes.length} notes · {query.data.edges.length} links
          </p>
          {query.data.limited && (
            <p className="rail-warning">Showing the first 40 notes.</p>
          )}
          <div className="graph-node-list">
            {query.data.nodes
              .filter((node) => node.id !== id)
              .map((node) => (
                <button key={node.id} onClick={() => w.open(node.id)}>
                  <span className="graph-depth">{node.depth}</span>
                  <span>
                    <strong>{node.title}</strong>
                    <small>{node.path}</small>
                  </span>
                </button>
              ))}
          </div>
          {query.data.nodes.length <= 1 && (
            <p className="rail-empty">This note has no resolved graph links.</p>
          )}
        </>
      )}
    </div>
  );
}
