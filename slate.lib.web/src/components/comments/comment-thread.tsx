"use client";
import { useState } from "react";
import dynamic from "next/dynamic";
import { formatDistanceToNowStrict } from "date-fns";
import { Bot, CheckCircle2, CircleDot, FileText, MessageSquareReply, RotateCcw, Trash2 } from "lucide-react";
import type { CommentThread, NoteComment } from "@/lib/api/contracts";
import { useWorkspace } from "@/features/notes/workspace-context";
import { Button } from "@/components/ui/button";
import type { NoteCommentActions } from "./use-note-comments";

const Markdown = dynamic(() => import("@/components/reader/markdown"), { ssr: false });

export function when(value: string) {
  try {
    const date = new Date(value);
    if (Date.now() - date.getTime() < 45_000) return "just now";
    return formatDistanceToNowStrict(date, { addSuffix: true });
  } catch {
    return "";
  }
}

export function Avatar({ comment, size = 32 }: { comment: NoteComment; size?: number }) {
  const agent = comment.author.kind === "agent";
  return (
    <span className={`comment-avatar ${agent ? "agent" : ""}`} style={{ width: size, height: size }} aria-hidden>
      {agent ? <Bot size={Math.round(size * 0.55)} /> : (comment.author.name.trim()[0] ?? "Y").toUpperCase()}
    </span>
  );
}

/** A comment box that grows with its text; Ctrl/Cmd+Enter submits. */
export function Composer({
  placeholder,
  submitLabel,
  busy,
  autoFocus,
  onSubmit,
  onCancel,
  compact,
}: {
  placeholder: string;
  submitLabel: string;
  busy?: boolean;
  autoFocus?: boolean;
  onSubmit: (body: string) => Promise<unknown>;
  onCancel?: () => void;
  compact?: boolean;
}) {
  const [body, setBody] = useState("");
  const [focused, setFocused] = useState(!!autoFocus);
  const [error, setError] = useState<string | null>(null);
  const submit = async () => {
    const text = body.trim();
    if (!text || busy) return;
    setError(null);
    try {
      await onSubmit(text);
      setBody("");
      if (!onCancel) setFocused(false);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "The comment could not be saved. Your text is kept.");
    }
  };
  return (
    <form
      className={`comment-composer ${compact ? "compact" : ""}`}
      onSubmit={(event) => {
        event.preventDefault();
        void submit();
      }}
    >
      <textarea
        aria-label={placeholder}
        placeholder={placeholder}
        value={body}
        rows={focused || body ? 3 : 1}
        maxLength={10_000}
        autoFocus={autoFocus}
        onFocus={() => setFocused(true)}
        onChange={(event) => setBody(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === "Enter" && (event.ctrlKey || event.metaKey)) {
            event.preventDefault();
            void submit();
          }
          if (event.key === "Escape" && onCancel) {
            event.preventDefault();
            onCancel();
          }
        }}
      />
      {error && <p className="comment-error" role="alert">{error}</p>}
      {(focused || body || onCancel) && (
        <div className="comment-composer-actions">
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() => {
              setBody("");
              setFocused(false);
              onCancel?.();
            }}
          >
            Cancel
          </Button>
          <Button type="submit" size="sm" disabled={!body.trim() || busy}>
            {busy ? "Saving…" : submitLabel}
          </Button>
        </div>
      )}
    </form>
  );
}

function CommentItem({
  comment,
  onDelete,
  onReply,
}: {
  comment: NoteComment;
  onDelete: () => void;
  onReply?: () => void;
}) {
  const w = useWorkspace();
  const agent = comment.author.kind === "agent";
  return (
    <div className="comment-item">
      <Avatar comment={comment} size={onReply ? 32 : 26} />
      <div className="comment-main">
        <header className="comment-meta">
          <strong>{comment.author.name}</strong>
          {agent && <span className="comment-badge">AI</span>}
          <time dateTime={comment.createdAt} title={new Date(comment.createdAt).toLocaleString()}>
            {when(comment.createdAt)}
          </time>
        </header>
        {comment.body && (
          <div className="comment-body">
            <Markdown source={comment.body} />
          </div>
        )}
        {comment.linkedNote && (
          <button className="comment-linked-note" onClick={() => w.open(comment.linkedNote!.noteId)}>
            <FileText size={16} />
            <span>
              <small>{agent ? "Wrote a page for this" : "Linked page"}</small>
              <strong>{comment.linkedNote.title ?? "Open note"}</strong>
            </span>
          </button>
        )}
        <div className="comment-actions">
          {onReply && (
            <button type="button" onClick={onReply}>
              <MessageSquareReply size={14} /> Reply
            </button>
          )}
          <button
            type="button"
            aria-label={`Delete comment by ${comment.author.name}`}
            onClick={() => {
              if (confirm(onReply ? "Delete this thread and all of its replies?" : "Delete this reply?")) onDelete();
            }}
          >
            <Trash2 size={14} />
          </button>
        </div>
      </div>
    </div>
  );
}

/**
 * A thread: the opening comment, its replies (collapsed past a few, like a video comment section), status, and a
 * reply box. Used both for passage questions and for the page discussion.
 */
export function ThreadView({
  thread,
  actions,
  showQuote,
  defaultReplying = false,
}: {
  thread: CommentThread;
  actions: NoteCommentActions;
  showQuote?: boolean;
  defaultReplying?: boolean;
}) {
  const [replying, setReplying] = useState(defaultReplying);
  const replies = thread.comments.slice(1);
  const [expanded, setExpanded] = useState(replies.length <= 2);
  const shown = expanded ? replies : replies.slice(-1);
  const resolved = thread.status === "resolved";
  return (
    <section className={`comment-thread ${resolved ? "resolved" : ""}`} aria-label="Comment thread">
      {showQuote && thread.anchor.kind === "text" && (
        <blockquote className={`comment-quote ${thread.anchorState === "detached" ? "detached" : ""}`}>
          {thread.anchor.exact}
          {thread.anchorState === "detached" && <small>This passage was changed or removed from the note.</small>}
        </blockquote>
      )}
      <div className="thread-state">
        {resolved ? (
          <span className="state-chip resolved"><CheckCircle2 size={13} /> Resolved</span>
        ) : thread.awaitingReply ? (
          <span className="state-chip waiting"><CircleDot size={13} /> Waiting for an answer</span>
        ) : (
          <span className="state-chip answered"><Bot size={13} /> Answered</span>
        )}
        <button
          type="button"
          className="thread-status-button"
          disabled={actions.status.isPending}
          onClick={() => actions.status.mutate({ threadId: thread.id, status: resolved ? "open" : "resolved" })}
        >
          {resolved ? <><RotateCcw size={13} /> Reopen</> : <><CheckCircle2 size={13} /> Resolve</>}
        </button>
      </div>
      <CommentItem
        comment={thread.comments[0]}
        onReply={() => setReplying(true)}
        onDelete={() => actions.remove.mutate({ threadId: thread.id, commentId: thread.comments[0].id })}
      />
      {replies.length > 0 && (
        <div className="comment-replies">
          {!expanded && (
            <button type="button" className="show-replies" onClick={() => setExpanded(true)}>
              Show {replies.length - 1} earlier repl{replies.length - 1 === 1 ? "y" : "ies"}
            </button>
          )}
          {shown.map((comment) => (
            <CommentItem
              key={comment.id}
              comment={comment}
              onDelete={() => actions.remove.mutate({ threadId: thread.id, commentId: comment.id })}
            />
          ))}
        </div>
      )}
      {replying && (
        <div className="comment-replies">
          <Composer
            compact
            autoFocus
            placeholder="Add a reply…"
            submitLabel="Reply"
            busy={actions.reply.isPending}
            onCancel={() => setReplying(false)}
            onSubmit={async (body) => {
              await actions.reply.mutateAsync({ threadId: thread.id, body });
              setReplying(false);
            }}
          />
        </div>
      )}
    </section>
  );
}
