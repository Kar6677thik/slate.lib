"use client";
import { useState } from "react";
import {
  useInfiniteQuery,
  useMutation,
  useQueryClient,
} from "@tanstack/react-query";
import {
  ChevronRight,
  Folder,
  FolderOpen,
  FileText,
  Plus,
  FolderPlus,
  RefreshCw,
} from "lucide-react";
import { useApi } from "@/lib/auth/context";
import { ApiError } from "@/lib/api/client";
import { useWorkspace } from "@/features/notes/workspace-context";
import {
  ErrorMessage,
  Loading,
  IconButton,
} from "@/components/common/primitives";
import { Button } from "@/components/ui/button";
import type { Entry } from "@/lib/api/contracts";
import { ItemMenu } from "./item-menu";
export function EntryActions({ entry }: { entry: Entry }) {
  return <ItemMenu entry={entry} />;
}
export function FolderChildren({
  path = "",
  depth = 0,
  foldersOnly = false,
  onFolder,
}: {
  path?: string;
  depth?: number;
  foldersOnly?: boolean;
  onFolder?: (path: string) => void;
}) {
  const api = useApi();
  const w = useWorkspace();
  const q = useInfiniteQuery({
    queryKey: ["folder", path],
    initialPageParam: 0,
    queryFn: ({ pageParam, signal }) => api.list(path, pageParam, signal),
    getNextPageParam: (last) => last.nextPage ?? undefined,
  });
  if (q.isPending) return <Loading label="Loading folder…" />;
  if (path === "inbox" && q.error instanceof ApiError && q.error.status === 404)
    return (
      <p className="tree-empty">
        Inbox is empty. Use Quick Thought to capture your first note.
      </p>
    );
  if (q.error)
    return <ErrorMessage error={q.error} retry={() => q.refetch()} />;
  const entries = q.data.pages
    .flatMap((p) => p.entries)
    .filter((e) => !foldersOnly || e.isDirectory);
  return (
    <div role="group">
      {entries.map((entry) =>
        entry.isDirectory ? (
          <FolderRow
            key={entry.path}
            entry={entry}
            depth={depth}
            foldersOnly={foldersOnly}
            onFolder={onFolder}
          />
        ) : (
          <div
            role="treeitem"
            aria-label={entry.title ?? entry.name.replace(/\.md$/i, "")}
            aria-selected={w.active === entry.id}
            className={`tree-row ${w.active === entry.id ? "selected" : ""}`}
            key={entry.path}
            style={{ paddingLeft: 8 + depth * 16 }}
          >
            <button
              className="tree-label"
              onClick={() => entry.id && w.open(entry.id)}
            >
              <FileText size={16} />
              <span>{entry.title ?? entry.name.replace(/\.md$/i, "")}</span>
            </button>
            <EntryActions entry={entry} />
          </div>
        ),
      )}
      {q.hasNextPage && (
        <Button
          variant="ghost"
          size="sm"
          disabled={q.isFetchingNextPage}
          onClick={() => q.fetchNextPage()}
        >
          Load more
        </Button>
      )}
      {entries.length === 0 && (
        <p className="tree-empty">
          No {foldersOnly ? "folders" : "items"} here yet.
        </p>
      )}
    </div>
  );
}
function FolderRow({
  entry,
  depth,
  foldersOnly,
  onFolder,
}: {
  entry: Entry;
  depth: number;
  foldersOnly: boolean;
  onFolder?: (path: string) => void;
}) {
  const [expanded, setExpanded] = useState(false);
  return (
    <div
      role="treeitem"
      aria-label={entry.name}
      aria-selected={false}
      aria-expanded={expanded}
    >
      <div className="tree-row" style={{ paddingLeft: 4 + depth * 16 }}>
        <button
          className="tree-label"
          onClick={() => {
            setExpanded(!expanded);
            onFolder?.(entry.path);
          }}
          onKeyDown={(e) => {
            if (e.key === "ArrowRight") {
              e.preventDefault();
              setExpanded(true);
            }
            if (e.key === "ArrowLeft") {
              e.preventDefault();
              setExpanded(false);
            }
          }}
        >
          <ChevronRight
            className={`chevron ${expanded ? "expanded" : ""}`}
            size={13}
          />
          {expanded ? <FolderOpen size={17} /> : <Folder size={17} />}
          <span>{entry.name}</span>
        </button>
        {!foldersOnly && <EntryActions entry={entry} />}
      </div>
      {expanded && (
        <FolderChildren
          path={entry.path}
          depth={depth + 1}
          foldersOnly={foldersOnly}
          onFolder={onFolder}
        />
      )}
    </div>
  );
}
export function Explorer() {
  const w = useWorkspace();
  const api = useApi(),
    cache = useQueryClient();
  const refresh = useMutation({
    mutationFn: () => api.refresh(),
    onSuccess: () => {
      void cache.invalidateQueries({ queryKey: ["folder"] });
      void cache.invalidateQueries({ queryKey: ["note"] });
      void cache.invalidateQueries({ queryKey: ["search"] });
    },
  });
  return (
    <section className="explorer">
      <div className="sidebar-heading">
        <span className="eyebrow">LIBRARY</span>
        <div className="inline-actions">
          <IconButton
            label="Refresh library"
            disabled={refresh.isPending}
            onClick={() => refresh.mutate()}
          >
            <RefreshCw size={15} />
          </IconButton>
          <IconButton
            label="New folder"
            onClick={() =>
              w.setOperation({ kind: "create-folder", folder: "" })
            }
          >
            <FolderPlus size={16} />
          </IconButton>
          <IconButton
            label="New note"
            onClick={() => w.setOperation({ kind: "create-note", folder: "" })}
          >
            <Plus size={17} />
          </IconButton>
        </div>
      </div>
      {refresh.error && <ErrorMessage error={refresh.error} />}
      <div
        className="tree-scroll"
        role="tree"
        aria-label="Library folders"
        onKeyDown={(e) => {
          if (e.key !== "ArrowDown" && e.key !== "ArrowUp") return;
          const items = Array.from(
            e.currentTarget.querySelectorAll<HTMLButtonElement>(".tree-label"),
          );
          const i = items.indexOf(document.activeElement as HTMLButtonElement);
          if (i >= 0) {
            e.preventDefault();
            items[
              Math.max(
                0,
                Math.min(
                  items.length - 1,
                  i + (e.key === "ArrowDown" ? 1 : -1),
                ),
              )
            ]?.focus();
          }
        }}
      >
        <FolderChildren />
      </div>
    </section>
  );
}
