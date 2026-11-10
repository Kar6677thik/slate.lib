"use client";
import { useMemo, useState } from "react";
import { MessageCircle, MessagesSquare } from "lucide-react";
import { Composer, ThreadView } from "./comment-thread";
import type { NoteCommentActions } from "./use-note-comments";

/**
 * The page-level discussion under a note, like the comment section under a video: questions and remarks about the
 * whole note, with agent answers and linked pages as replies.
 */
export function Discussion({
  actions,
  passageCount,
  onShowPassages,
}: {
  actions: NoteCommentActions;
  passageCount: number;
  onShowPassages: () => void;
}) {
  const [order, setOrder] = useState<"newest" | "oldest">("newest");
  const [showHidden, setShowHidden] = useState(false);
  const hiddenCount = actions.query.data?.threads.filter((thread) => thread.anchorState === "page" && thread.hidden).length ?? 0;
  const threads = useMemo(() => {
    const page = (actions.query.data?.threads ?? []).filter((thread) => thread.anchorState === "page");
    // Resolved threads stay; only threads the owner hid are left out unless asked for.
    const visible = showHidden ? page : page.filter((thread) => !thread.hidden);
    return [...visible].sort((a, b) =>
      order === "newest" ? b.createdAt.localeCompare(a.createdAt) : a.createdAt.localeCompare(b.createdAt),
    );
  }, [actions.query.data, order, showHidden]);
  const total = (actions.query.data?.threads.filter((thread) => thread.anchorState === "page").length ?? 0) - hiddenCount;
  return (
    <section className="note-discussion" aria-labelledby="note-discussion-title">
      <header className="discussion-header">
        <h2 id="note-discussion-title">
          <MessagesSquare size={18} /> Discussion{total ? ` · ${total}` : ""}
        </h2>
        <div className="discussion-tools">
          {passageCount > 0 && (
            <button type="button" className="discussion-link" onClick={onShowPassages}>
              <MessageCircle size={14} /> {passageCount} on passages
            </button>
          )}
          {total > 1 && (
            <select aria-label="Sort comments" value={order} onChange={(event) => setOrder(event.target.value as "newest" | "oldest")}>
              <option value="newest">Newest first</option>
              <option value="oldest">Oldest first</option>
            </select>
          )}
          {hiddenCount > 0 && (
            <label className="discussion-toggle">
              <input type="checkbox" checked={showHidden} onChange={(event) => setShowHidden(event.target.checked)} />
              Show hidden ({hiddenCount})
            </label>
          )}
        </div>
      </header>
      <Composer
        placeholder="Ask a question or leave a note for future you…"
        submitLabel="Comment"
        busy={actions.create.isPending}
        onSubmit={(body) => actions.create.mutateAsync({ anchor: { kind: "page" }, body })}
      />
      {actions.query.isPending ? (
        <p className="muted">Loading comments…</p>
      ) : actions.query.error ? (
        <p className="muted" role="alert">
          Comments are unavailable right now.{" "}
          <button type="button" className="discussion-link" onClick={() => void actions.query.refetch()}>Try again</button>
        </p>
      ) : threads.length === 0 ? (
        <p className="muted discussion-empty">
          {hiddenCount ? "Every comment here is hidden." : "No comments yet. Questions you leave here can be answered by your AI assistant through Slate's MCP server."}
        </p>
      ) : (
        <div className="discussion-threads">
          {threads.map((thread) => (
            <ThreadView key={thread.id} thread={thread} actions={actions} />
          ))}
        </div>
      )}
    </section>
  );
}
