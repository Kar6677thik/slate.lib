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
  Star,
  Pin,
  Search,
  X,
  CalendarDays,
  CircleHelp,
  ArchiveRestore,
  Compass,
  Unlink,
  ListChecks,
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
import { SMART_VIEWS } from "@/features/views/smart-views";
import { useWorkspacePreferences } from "@/lib/storage/workspace-preferences";
import { Modal } from "@/components/common/primitives";
import { format } from "date-fns";
import { BulkActions } from "./bulk-actions";

type Selection = {
  paths: ReadonlySet<string>;
  toggle: (path: string) => void;
};
export function EntryActions({ entry }: { entry: Entry }) {
  return <ItemMenu entry={entry} />;
}
export function FolderChildren({
  path = "",
  depth = 0,
  foldersOnly = false,
  onFolder,
  selection,
}: {
  path?: string;
  depth?: number;
  foldersOnly?: boolean;
  onFolder?: (path: string) => void;
  selection?: Selection;
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
            selection={selection}
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
            {selection && (
              <input
                className="tree-select"
                type="checkbox"
                aria-label={`Select ${entry.title ?? entry.name}`}
                checked={selection.paths.has(entry.path)}
                onChange={() => selection.toggle(entry.path)}
              />
            )}
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
  selection,
}: {
  entry: Entry;
  depth: number;
  foldersOnly: boolean;
  onFolder?: (path: string) => void;
  selection?: Selection;
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
        {selection && (
          <input
            className="tree-select"
            type="checkbox"
            aria-label={`Select ${entry.name}`}
            checked={selection.paths.has(entry.path)}
            onChange={() => selection.toggle(entry.path)}
          />
        )}
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
          selection={selection}
        />
      )}
    </div>
  );
}
export function Explorer() {
  const w = useWorkspace();
  const local = useWorkspacePreferences(w.scope);
  const [questionOpen, setQuestionOpen] = useState(false);
  const [selecting, setSelecting] = useState(false);
  const [selected, setSelected] = useState<Set<string>>(() => new Set());
  const [question, setQuestion] = useState({
    folder: "",
    name: "",
    title: "",
    body: "",
  });
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
  const daily = useMutation({
    mutationFn: () => api.daily(format(new Date(), "yyyy-MM-dd")),
    onSuccess: (note) => {
      void cache.invalidateQueries({ queryKey: ["folder"] });
      void cache.invalidateQueries({ queryKey: ["smart-view"] });
      w.open(note.id);
    },
  });
  const createQuestion = useMutation({
    mutationFn: () =>
      api.question(
        question.folder.trim(),
        question.name.trim(),
        question.title.trim(),
        question.body,
      ),
    onSuccess: (note) => {
      void cache.invalidateQueries({ queryKey: ["folder"] });
      void cache.invalidateQueries({ queryKey: ["smart-view"] });
      setQuestionOpen(false);
      setQuestion({ folder: "", name: "", title: "", body: "" });
      w.open(note.id);
    },
  });
  return (
    <section className="explorer">
      <div className="knowledge-actions">
        <button disabled={daily.isPending} onClick={() => daily.mutate()}>
          <CalendarDays size={15} />
          <span>{daily.isPending ? "Opening today…" : "Daily note"}</span>
        </button>
        <button onClick={() => setQuestionOpen(true)}>
          <CircleHelp size={15} />
          <span>New question</span>
        </button>
      </div>
      {(daily.error || createQuestion.error) && (
        <ErrorMessage error={daily.error ?? createQuestion.error} />
      )}
      {(local.preferences.favorites.length > 0 ||
        local.preferences.pins.length > 0 ||
        local.preferences.searches.length > 0) && (
        <div className="personal-nav" aria-label="Personal shortcuts">
          <span className="eyebrow">SHORTCUTS</span>
          {local.preferences.favorites.map((favorite) => (
            <button key={favorite.id} onClick={() => w.open(favorite.id)}>
              <Star size={14} fill="currentColor" />
              <span>{favorite.title}</span>
            </button>
          ))}
          {local.preferences.pins.map((pin) => (
            <PinnedFolder key={pin.path} pin={pin} />
          ))}
          {local.preferences.searches.map((search) => (
            <div className="personal-nav-row" key={search.id}>
              <button
                onClick={() => {
                  w.setQuery(search.query);
                  w.navigate("search");
                }}
              >
                <Search size={14} />
                <span>{search.name}</span>
              </button>
              <IconButton
                label={`Remove saved search ${search.name}`}
                onClick={() => local.deleteSearch(search.id)}
              >
                <X size={13} />
              </IconButton>
            </div>
          ))}
        </div>
      )}
      <div className="smart-view-nav" aria-label="Smart views">
        <span className="eyebrow">VIEWS</span>
        {SMART_VIEWS.map(({ id, title, icon: Icon }) => (
          <button
            key={id}
            className={w.nav === id ? "active" : ""}
            onClick={() => w.navigate(id)}
          >
            <Icon size={15} />
            <span>{title}</span>
          </button>
        ))}
        <button
          className={w.nav === "rediscover" ? "active" : ""}
          onClick={() => w.navigate("rediscover")}
        >
          <Compass size={15} />
          <span>Rediscover</span>
        </button>
        <button
          className={w.nav === "recovery" ? "active" : ""}
          onClick={() => w.navigate("recovery")}
        >
          <ArchiveRestore size={15} />
          <span>Deleted notes</span>
        </button>
        <button
          className={w.nav === "link-health" ? "active" : ""}
          onClick={() => w.navigate("link-health")}
        >
          <Unlink size={15} />
          <span>Link health</span>
        </button>
      </div>
      <div className="sidebar-heading">
        <span className="eyebrow">LIBRARY</span>
        <div className="inline-actions">
          <IconButton
            label={selecting ? "Stop selecting" : "Select items"}
            className={selecting ? "active" : ""}
            onClick={() => {
              setSelecting((value) => !value);
              setSelected(new Set());
            }}
          >
            <ListChecks size={16} />
          </IconButton>
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
        <FolderChildren
          selection={
            selecting
              ? {
                  paths: selected,
                  toggle(path) {
                    setSelected((current) => {
                      const next = new Set(current);
                      if (next.has(path)) next.delete(path);
                      else next.add(path);
                      return next;
                    });
                  },
                }
              : undefined
          }
        />
      </div>
      {selecting && (
        <BulkActions
          paths={[...selected]}
          onDone={() => {
            setSelected(new Set());
            setSelecting(false);
          }}
          onCancel={() => {
            setSelected(new Set());
            setSelecting(false);
          }}
        />
      )}
      <Modal
        open={questionOpen}
        onClose={() => !createQuestion.isPending && setQuestionOpen(false)}
        title="New question"
        description="Create a question note that stays visible until you answer it."
      >
        <form
          className="form-stack"
          onSubmit={(event) => {
            event.preventDefault();
            createQuestion.mutate();
          }}
        >
          <label>
            Question
            <input
              autoFocus
              required
              maxLength={200}
              value={question.title}
              onChange={(event) =>
                setQuestion((value) => ({
                  ...value,
                  title: event.target.value,
                  name: value.name || event.target.value,
                }))
              }
              placeholder="What do you want to understand?"
            />
          </label>
          <label>
            Note name
            <input
              required
              pattern="[^/\\]+"
              value={question.name}
              onChange={(event) =>
                setQuestion((value) => ({ ...value, name: event.target.value }))
              }
            />
          </label>
          <label>
            Folder
            <input
              value={question.folder}
              onChange={(event) =>
                setQuestion((value) => ({
                  ...value,
                  folder: event.target.value,
                }))
              }
              placeholder="Library root"
            />
          </label>
          <label>
            Context <span className="muted">optional</span>
            <textarea
              rows={5}
              value={question.body}
              onChange={(event) =>
                setQuestion((value) => ({ ...value, body: event.target.value }))
              }
              placeholder="Add what prompted the question or what you already know."
            />
          </label>
          <div className="dialog-actions">
            <Button type="button" variant="outline" onClick={() => setQuestionOpen(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={createQuestion.isPending}>
              {createQuestion.isPending ? "Creating…" : "Create question"}
            </Button>
          </div>
        </form>
      </Modal>
    </section>
  );
}

function PinnedFolder({ pin }: { pin: { path: string; label: string } }) {
  const [expanded, setExpanded] = useState(false);
  return (
    <div className="pinned-folder">
      <button onClick={() => setExpanded((value) => !value)}>
        <ChevronRight
          className={`chevron ${expanded ? "expanded" : ""}`}
          size={13}
        />
        <Pin size={14} />
        <span>{pin.label}</span>
      </button>
      {expanded && <FolderChildren path={pin.path} depth={1} />}
    </div>
  );
}
