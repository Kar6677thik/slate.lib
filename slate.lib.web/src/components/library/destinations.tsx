"use client";
import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { FileText, Clock3, Plus, ArrowUpRight } from "lucide-react";
import { formatDistanceToNow } from "date-fns";
import { useWorkspace } from "@/features/notes/workspace-context";
import { readRecent } from "@/lib/storage/recent";
import { FolderChildren } from "./explorer";
import { Button } from "@/components/ui/button";
import { Empty } from "@/components/common/primitives";
import { ErrorMessage, Loading } from "@/components/common/primitives";
import { useApi } from "@/lib/auth/context";
import { smartView } from "@/features/views/smart-views";
import { RecoveryPage } from "@/components/history/recovery-page";
import { RediscoveryPage } from "./rediscovery-page";
import { LinkHealthPage } from "@/components/links/link-health-page";
export function DestinationPage() {
  const w = useWorkspace();
  const [recent] = useState(() => readRecent(w.scope));
  if (w.nav === "recovery") return <RecoveryPage />;
  if (w.nav === "rediscover") return <RediscoveryPage />;
  if (w.nav === "link-health") return <LinkHealthPage />;
  const view = smartView(w.nav);
  if (view) return <SmartViewPage view={view} />;
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

function SmartViewPage({
  view,
}: {
  view: NonNullable<ReturnType<typeof smartView>>;
}) {
  const api = useApi();
  const w = useWorkspace();
  const [page, setPage] = useState(0);
  useEffect(() => setPage(0), [view.id]);
  const query = useQuery({
    queryKey: ["smart-view", view.id, page],
    queryFn: ({ signal }) => api.smartView(view.id, page, signal),
  });
  const Icon = view.icon;
  return (
    <>
      <div className="view-header smart-view-header">
        <p className="eyebrow">SMART VIEW</p>
        <div className="section-heading">
          <h1>{view.title}</h1>
          <Icon size={20} className="muted" />
        </div>
        <p>{view.description}</p>
      </div>
      <div className="view-body scroll-area">
        {query.isPending ? (
          <Loading label={`Loading ${view.title.toLowerCase()}…`} />
        ) : query.error ? (
          <ErrorMessage error={query.error} retry={() => query.refetch()} />
        ) : (
          <>
            <div className="results-meta">
              {query.data.total.toLocaleString()} notes
              <span>Server-defined view</span>
            </div>
            {query.data.results.map((result) => (
              <button
                className="result-row"
                key={result.id}
                onClick={() => w.open(result.id)}
              >
                <FileText size={19} />
                <div>
                  <strong>{result.title}</strong>
                  <span className="result-path">{result.path}</span>
                  {result.snippet && (
                    <p>{result.snippet.replace(/<[^>]*>/g, "")}</p>
                  )}
                </div>
                <ArrowUpRight size={15} />
              </button>
            ))}
            {query.data.total === 0 && (
              <Empty
                title={`No ${view.title.toLowerCase()}`}
                detail="This view updates automatically as your library changes."
              />
            )}
            {query.data.total > query.data.pageSize && (
              <div className="pagination">
                <Button
                  variant="ghost"
                  size="sm"
                  disabled={page === 0}
                  onClick={() => setPage((value) => value - 1)}
                >
                  Previous
                </Button>
                <span>Page {page + 1}</span>
                <Button
                  variant="ghost"
                  size="sm"
                  disabled={(page + 1) * query.data.pageSize >= query.data.total}
                  onClick={() => setPage((value) => value + 1)}
                >
                  Next
                </Button>
              </div>
            )}
          </>
        )}
      </div>
    </>
  );
}
