"use client";
import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  FileText,
  Clock3,
  Plus,
  ArrowUpRight,
  Star,
  Search,
  Zap,
  Compass,
  ArchiveRestore,
  Unlink,
  type LucideIcon,
} from "lucide-react";
import { formatDistanceToNow } from "date-fns";
import { useWorkspace } from "@/features/notes/workspace-context";
import { readRecent } from "@/lib/storage/recent";
import { FolderChildren } from "./explorer";
import { Button } from "@/components/ui/button";
import { Empty } from "@/components/common/primitives";
import { ErrorMessage, Loading } from "@/components/common/primitives";
import { useApi, useAuth } from "@/lib/auth/context";
import { SMART_VIEWS, smartView } from "@/features/views/smart-views";
import type { Destination } from "@/components/layout/shell";
import { RecoveryPage } from "@/components/history/recovery-page";
import { RediscoveryPage } from "./rediscovery-page";
import { LinkHealthPage } from "@/components/links/link-health-page";
import { useWorkspacePreferences } from "@/lib/storage/workspace-preferences";
import { InboxTriage } from "@/components/inbox/inbox-triage";
import { usePreferences } from "@/lib/storage/preferences";
export function DestinationPage() {
  const { prefs } = usePreferences();
  const w = useWorkspace();
  const local = useWorkspacePreferences(w.scope);
  const [recent] = useState(() => readRecent(w.scope));
  if (w.nav === "recovery") return <RecoveryPage />;
  if (w.nav === "rediscover") return <RediscoveryPage />;
  if (w.nav === "link-health") return <LinkHealthPage />;
  if (w.nav === "favorites")
    return (
      <>
        <div className="view-header">
          <p className="eyebrow">PERSONAL VIEW</p>
          <h1>Favorites</h1>
          <p>Notes you have starred in this browser.</p>
        </div>
        <div className="scroll-area view-body">
          {local.preferences.favorites.length ? (
            local.preferences.favorites.map((favorite) => (
              <button
                className="result-row"
                key={favorite.id}
                onClick={() => w.open(favorite.id)}
              >
                <Star size={18} fill="currentColor" />
                <div>
                  <strong>{favorite.title}</strong>
                  <span className="result-path">{favorite.path}</span>
                </div>
                <ArrowUpRight size={15} />
              </button>
            ))
          ) : (
            <Empty
              title="No favorites"
              detail="Star a note from its menu or the Command Center."
            />
          )}
        </div>
      </>
    );
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
  if (inbox && prefs.aiEnabled) return <InboxTriage />;
  if (!inbox) return <LibraryHome />;
  return (
    <>
      <div className="view-header">
        <p className="eyebrow">CAPTURE &amp; ORGANIZE</p>
        <div className="section-heading">
          <h1>Inbox</h1>
          <Button
            variant="outline"
            size="sm"
            onClick={() => w.setCaptureOpen(true)}
          >
            <Plus size={15} />
            Capture
          </Button>
        </div>
        <p>Capture now. Organize when you’re ready.</p>
      </div>
      <div className="view-body scroll-area">
        <div className="section-heading">
          <h2>Unsorted thoughts</h2>
          <Clock3 size={14} className="muted" />
        </div>
        <div className="library-browser" role="tree" aria-label="Inbox notes">
          <FolderChildren key={w.nav} path="inbox" />
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
                detail={
                  ("emptyHint" in view ? view.emptyHint : undefined) ??
                  "This view updates automatically as your library changes."
                }
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
                  disabled={
                    (page + 1) * query.data.pageSize >= query.data.total
                  }
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

const LIBRARY_VIEWS: {
  id: Destination;
  title: string;
  description: string;
  icon: LucideIcon;
}[] = [
  ...SMART_VIEWS,
  {
    id: "rediscover",
    title: "Rediscover",
    description: "Older notes worth another look.",
    icon: Compass,
  },
  {
    id: "link-health",
    title: "Link health",
    description: "Broken, ambiguous, and unresolved links.",
    icon: Unlink,
  },
  {
    id: "recovery",
    title: "Deleted notes",
    description: "Restore notes from Git history.",
    icon: ArchiveRestore,
  },
];

/** The library landing page: everything that isn't already in the file tree, so nothing is shown twice. */
function LibraryHome() {
  const w = useWorkspace();
  const { api, connection } = useAuth();
  const local = useWorkspacePreferences(w.scope);
  const [recent] = useState(() => readRecent(w.scope).slice(0, 6));
  const status = useQuery({
    queryKey: ["status", connection?.server],
    queryFn: ({ signal }) => api!.status(signal),
    enabled: !!api,
  });
  const favorites = local.preferences.favorites;
  return (
    <div className="library-home scroll-area">
      <div className="home-inner">
        <header className="home-header">
          <div>
            <p className="eyebrow">WORKSPACE</p>
            <h1>Library</h1>
            <p className="muted">
              {status.data
                ? `${status.data.noteCount.toLocaleString()} note${status.data.noteCount === 1 ? "" : "s"}`
                : "Your notes"}
            </p>
          </div>
          <div className="home-actions">
            <Button
              variant="outline"
              onClick={() => w.setCaptureOpen(true)}
              aria-label="Capture a thought"
            >
              <Zap size={15} />
              Capture
            </Button>
            <Button
              onClick={() => w.setOperation({ kind: "create-note", folder: "" })}
              aria-label="Create note"
            >
              <Plus size={15} />
              New note
            </Button>
          </div>
        </header>
        <button className="home-search" onClick={() => w.openCommandCenter()}>
          <Search size={17} />
          <span>Search notes, or type &gt; for commands</span>
          <kbd>Ctrl K</kbd>
        </button>
        {recent.length > 0 && (
          <section className="home-section" aria-labelledby="home-recent">
            <div className="home-section-heading">
              <h2 id="home-recent">Jump back in</h2>
              <Button variant="ghost" size="sm" onClick={() => w.navigate("recent")}>
                View all
              </Button>
            </div>
            <div className="home-grid">
              {recent.map((r) => (
                <button className="home-card" key={r.id} onClick={() => w.open(r.id)}>
                  <FileText size={17} />
                  <strong>{r.title}</strong>
                  <span className="result-path">{r.path}</span>
                  <small>
                    {formatDistanceToNow(new Date(r.openedAt), { addSuffix: true })}
                  </small>
                </button>
              ))}
            </div>
          </section>
        )}
        {favorites.length > 0 && (
          <section className="home-section" aria-labelledby="home-favorites">
            <div className="home-section-heading">
              <h2 id="home-favorites">Favorites</h2>
            </div>
            <div className="home-grid">
              {favorites.map((favorite) => (
                <button className="home-card" key={favorite.id} onClick={() => w.open(favorite.id)}>
                  <Star size={17} fill="currentColor" />
                  <strong>{favorite.title}</strong>
                  <span className="result-path">{favorite.path}</span>
                </button>
              ))}
            </div>
          </section>
        )}
        <section className="home-section home-folders" aria-labelledby="home-folders">
          <div className="home-section-heading">
            <h2 id="home-folders">Folders</h2>
          </div>
          <div className="library-browser" role="tree" aria-label="Browse library">
            <FolderChildren path="" />
          </div>
        </section>
        <section className="home-section" aria-labelledby="home-views">
          <div className="home-section-heading">
            <h2 id="home-views">Views</h2>
          </div>
          <div className="home-grid home-views">
            {LIBRARY_VIEWS.map(({ id, title, description, icon: Icon }) => (
              <button
                className="home-card home-view"
                key={id}
                aria-label={title}
                onClick={() => w.navigate(id)}
              >
                <Icon size={17} />
                <strong>{title}</strong>
                <span>{description}</span>
              </button>
            ))}
          </div>
        </section>
        {recent.length === 0 && favorites.length === 0 && (
          <p className="home-hint muted">
            Open a note from the file tree to start. Recently opened notes and
            favorites will appear here.
          </p>
        )}
      </div>
    </div>
  );
}
