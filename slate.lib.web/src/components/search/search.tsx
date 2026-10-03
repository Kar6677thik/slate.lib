"use client";
import { useState, useEffect } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  Search,
  FileText,
  ArrowUpRight,
  Plus,
  FolderPlus,
  RefreshCw,
  Zap,
  Moon,
  Settings,
} from "lucide-react";
import { useTheme } from "next-themes";
import { useApi } from "@/lib/auth/context";
import { useWorkspace } from "@/features/notes/workspace-context";
import { useDebounce } from "@/hooks/use-debounce";
import {
  Modal,
  Loading,
  ErrorMessage,
  Empty,
} from "@/components/common/primitives";
import { Button } from "@/components/ui/button";
export function SearchResults({ query }: { query: string }) {
  const api = useApi(),
    w = useWorkspace();
  const [page, setPage] = useState(0);
  const term = useDebounce(query);
  useEffect(() => setPage(0), [term]);
  const q = useQuery({
    queryKey: ["search", term, page],
    queryFn: ({ signal }) => api.search(term, page, signal),
    enabled: !!term.trim(),
  });
  if (!term.trim())
    return (
      <Empty
        title="Search your library"
        detail="Search titles, content, paths, aliases, or tags. Try type:question status:open."
      />
    );
  if (q.isPending) return <Loading label="Searching your library…" />;
  if (q.error)
    return <ErrorMessage error={q.error} retry={() => q.refetch()} />;
  return (
    <>
      <div className="results-meta">
        {q.data.total.toLocaleString()} results <span>Ranked by relevance</span>
      </div>
      <div
        className="results-list"
        role="list"
        onKeyDown={(e) => {
          if (e.key !== "ArrowDown" && e.key !== "ArrowUp") return;
          const items = Array.from(
            e.currentTarget.querySelectorAll<HTMLButtonElement>(".result-row"),
          );
          const i = items.indexOf(document.activeElement as HTMLButtonElement);
          e.preventDefault();
          items[
            Math.max(
              0,
              Math.min(items.length - 1, i + (e.key === "ArrowDown" ? 1 : -1)),
            )
          ]?.focus();
        }}
      >
        {q.data.results.map((r) => (
          <button
            role="listitem"
            className="result-row"
            key={r.id}
            onClick={() => w.open(r.id)}
          >
            <FileText size={19} />
            <div>
              <strong>{r.title}</strong>
              <span className="result-path">{r.path}</span>
              <p>{r.snippet.replace(/<[^>]*>/g, "")}</p>
            </div>
            <ArrowUpRight size={15} />
          </button>
        ))}
      </div>
      {q.data.total === 0 && (
        <Empty
          title="No matching notes"
          detail="Try fewer words or remove a filter."
        />
      )}
      <div className="pagination">
        <Button
          variant="ghost"
          size="sm"
          disabled={page === 0}
          onClick={() => setPage(page - 1)}
        >
          Previous
        </Button>
        <span>Page {page + 1}</span>
        <Button
          variant="ghost"
          size="sm"
          disabled={(page + 1) * q.data.pageSize >= q.data.total}
          onClick={() => setPage(page + 1)}
        >
          Next
        </Button>
      </div>
    </>
  );
}
export function SearchPage() {
  const w = useWorkspace();
  return (
    <>
      <div className="view-header">
        <p className="eyebrow">ALL NOTES</p>
        <h1>Search</h1>
        <div className="search-field">
          <Search size={18} />
          <input
            autoFocus
            aria-label="Search library"
            placeholder="Search titles, content, paths, or tags…"
            value={w.query}
            onChange={(e) => w.setQuery(e.target.value)}
          />
        </div>
      </div>
      <div className="view-body scroll-area">
        <SearchResults query={w.query} />
      </div>
    </>
  );
}
export function QuickOpen() {
  const w = useWorkspace(),
    api = useApi();
  const [query, setQuery] = useState(""),
    [error, setError] = useState<unknown>(null);
  const { setTheme, theme } = useTheme();
  return (
    <Modal
      open={w.searchOpen}
      onClose={() => w.setSearchOpen(false)}
      title="Search & commands"
      description="Search your notes or choose a workspace action."
      wide
    >
      <div className="search-field">
        <Search size={18} />
        <input
          autoFocus
          aria-label="Quick search"
          placeholder="Search the library…"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
      </div>
      {error != null && <ErrorMessage error={error} />}
      <div
        className="quick-results"
        onKeyDown={(e) => {
          if (e.key !== "ArrowDown" && e.key !== "ArrowUp") return;
          const items = Array.from(
            e.currentTarget.querySelectorAll<HTMLButtonElement>(".command-row"),
          );
          if (!items.length) return;
          e.preventDefault();
          const index = items.indexOf(
            document.activeElement as HTMLButtonElement,
          );
          items[
            (index + (e.key === "ArrowDown" ? 1 : -1) + items.length) %
              items.length
          ]?.focus();
        }}
      >
        {query ? (
          <SearchResults query={query} />
        ) : (
          <>
            <p className="section-label">WORKSPACE ACTIONS</p>
            {[
              {
                label: "New note",
                icon: Plus,
                run: () => w.setOperation({ kind: "create-note", folder: "" }),
              },
              {
                label: "New folder",
                icon: FolderPlus,
                run: () =>
                  w.setOperation({ kind: "create-folder", folder: "" }),
              },
              {
                label: "Quick Thought",
                icon: Zap,
                run: () => w.setCaptureOpen(true),
              },
              {
                label: "Search library",
                icon: Search,
                run: () => w.navigate("search"),
              },
              {
                label: "Sync library",
                icon: RefreshCw,
                run: () => api.sync(),
              },
              {
                label: "Settings",
                icon: Settings,
                run: () => w.setSettingsOpen(true),
              },
              {
                label: "Toggle theme",
                icon: Moon,
                run: () => setTheme(theme === "dark" ? "light" : "dark"),
              },
            ].map((c) => (
              <button
                className="command-row"
                key={c.label}
                onClick={async () => {
                  try {
                    await c.run();
                    w.setSearchOpen(false);
                  } catch (error) {
                    setError(error);
                  }
                }}
              >
                <c.icon size={18} />
                {c.label}
                <ArrowUpRight size={14} />
              </button>
            ))}
          </>
        )}
      </div>
      <div className="overlay-hint">
        <kbd>↑ ↓</kbd> navigate <kbd>Enter</kbd> open <kbd>Esc</kbd> close
      </div>
    </Modal>
  );
}
