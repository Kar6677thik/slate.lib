"use client";
import { useEffect, useMemo, useRef, useState, type RefObject } from "react";
import { MessageSquarePlus, X } from "lucide-react";
import type { CommentAnchor, CommentThread } from "@/lib/api/contracts";
import { buildTextIndex, locateQuote, offsetOf, quoteFromRange, rangeFor, type TextIndex } from "@/lib/comments/anchoring";
import { Composer, ThreadView } from "./comment-thread";
import type { NoteCommentActions } from "./use-note-comments";

const BLOCKS = "p, li, h1, h2, h3, h4, h5, h6, blockquote, td, th, pre, figure, .code-block";
const HIGHLIGHT = "slate-comment";
const ACTIVE = "slate-comment-active";

type Anchored = { thread: CommentThread; start: number; end: number; range: Range };
type Bubble = { key: string; top: number; left: number; threads: CommentThread[] };
export type DrawerState = { focus: string[] | null; compose: { anchor: CommentAnchor } | null } | null;

type HighlightRegistry = { set(name: string, value: unknown): void; delete(name: string): void };
function registry(): HighlightRegistry | null {
  const css = (globalThis as { CSS?: { highlights?: HighlightRegistry } }).CSS;
  return css?.highlights && typeof (globalThis as { Highlight?: unknown }).Highlight === "function" ? css.highlights : null;
}
function paint(name: string, ranges: Range[]) {
  const highlights = registry();
  if (!highlights) return;
  if (!ranges.length) highlights.delete(name);
  else highlights.set(name, new (globalThis as unknown as { Highlight: new (...r: Range[]) => unknown }).Highlight(...ranges));
}

function caretOffset(index: TextIndex, x: number, y: number) {
  const doc = document as Document & {
    caretPositionFromPoint?: (x: number, y: number) => { offsetNode: Node; offset: number } | null;
    caretRangeFromPoint?: (x: number, y: number) => Range | null;
  };
  const position = doc.caretPositionFromPoint?.(x, y);
  if (position) return offsetOf(index, position.offsetNode, position.offset);
  const range = doc.caretRangeFromPoint?.(x, y);
  return range ? offsetOf(index, range.startContainer, range.startOffset) : null;
}

function articleIn(container: HTMLElement | null) {
  return container?.querySelector<HTMLElement>("article.markdown") ?? null;
}

/** One margin bubble per block that holds open passage threads, positioned in the article's right padding. */
function bubblesFor(container: HTMLElement, items: Anchored[]): Bubble[] {
  const body = articleIn(container);
  if (!body) return [];
  const base = container.getBoundingClientRect();
  const right = body.getBoundingClientRect().right;
  const padding = parseFloat(getComputedStyle(body).paddingRight) || 0;
  const groups = new Map<Element, CommentThread[]>();
  for (const item of items) {
    if (item.thread.status !== "open") continue;
    const start = item.range.startContainer;
    const element = (start instanceof Element ? start : start.parentElement)?.closest(BLOCKS) ?? body;
    groups.set(element, [...(groups.get(element) ?? []), item.thread]);
  }
  return [...groups.entries()].map(([element, group]) => ({
    key: group.map((thread) => thread.id).join(":"),
    top: element.getBoundingClientRect().top - base.top,
    // Centered in the article's right padding (the bubble is translated by half its width).
    left: right - base.left - Math.min(padding, 48) / 2,
    threads: group,
  }));
}

/**
 * Passage comments over the rendered note: select text to ask about it, see highlighted passages with a count
 * bubble in the margin (one bubble per paragraph, as in serialized-fiction readers), and open the thread drawer.
 * Highlights use the CSS Custom Highlight API, so the rendered note DOM is never modified.
 */
export function ReaderComments({
  root,
  actions,
  drawer,
  setDrawer,
  renderKey,
}: {
  root: RefObject<HTMLDivElement | null>;
  actions: NoteCommentActions;
  drawer: DrawerState;
  setDrawer: (state: DrawerState) => void;
  /** Changes whenever the rendered note text may have changed. */
  renderKey: string;
}) {
  const threads = useMemo(() => (actions.query.data?.threads ?? []).filter((thread) => thread.anchor.kind === "text"), [actions.query.data]);
  const [anchored, setAnchored] = useState<Anchored[]>([]);
  const [bubbles, setBubbles] = useState<Bubble[]>([]);
  const [selection, setSelection] = useState<{ anchor: CommentAnchor; x: number; y: number } | null>(null);
  const [showResolved, setShowResolved] = useState(false);
  const [active, setActive] = useState<string | null>(null);
  const index = useRef<TextIndex | null>(null);

  // Re-anchor whenever the note renders differently or the threads change.
  useEffect(() => {
    const container = root.current;
    if (!container) return;
    let frame = 0;
    const run = () => {
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(() => {
        const body = articleIn(container);
        if (!body) return;
        const current = buildTextIndex(body);
        index.current = current;
        const items: Anchored[] = [];
        for (const thread of threads) {
          const span = locateQuote(current.text, thread.anchor);
          const range = span && rangeFor(current, span.start, span.end);
          if (span && range) items.push({ thread, ...span, range });
        }
        setAnchored(items);
        setBubbles(bubblesFor(container, items));
      });
    };
    run();
    // The comment layer itself lives in the container; its own updates must not trigger re-anchoring.
    const observer = new MutationObserver((records) => {
      const outside = records.some((record) => {
        const target = record.target instanceof Element ? record.target : record.target.parentElement;
        return !target?.closest(".comment-layer");
      });
      if (outside) run();
    });
    observer.observe(container, { childList: true, subtree: true, characterData: true });
    const resize = new ResizeObserver(run);
    resize.observe(container);
    return () => {
      cancelAnimationFrame(frame);
      observer.disconnect();
      resize.disconnect();
    };
  }, [threads, renderKey, root]);

  useEffect(() => {
    paint(HIGHLIGHT, anchored.filter((item) => item.thread.status === "open" && item.thread.id !== active).map((item) => item.range));
    paint(ACTIVE, anchored.filter((item) => item.thread.id === active).map((item) => item.range));
  }, [anchored, active]);
  useEffect(() => () => { paint(HIGHLIGHT, []); paint(ACTIVE, []); }, []);
  useEffect(() => {
    if (!drawer) {
      setActive(null);
      return;
    }
    const escape = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !event.defaultPrevented) setDrawer(null);
    };
    document.addEventListener("keydown", escape);
    return () => document.removeEventListener("keydown", escape);
  }, [drawer, setDrawer]);

  // Offer "Ask about this" for a selection inside the note.
  useEffect(() => {
    let timer = 0;
    const update = () => {
      window.clearTimeout(timer);
      timer = window.setTimeout(() => {
        const body = articleIn(root.current);
        const current = window.getSelection();
        if (!body || !current || current.isCollapsed || current.rangeCount === 0) return setSelection(null);
        const range = current.getRangeAt(0);
        if (!body.contains(range.commonAncestorContainer)) return setSelection(null);
        const anchor = quoteFromRange(buildTextIndex(body), range);
        const rect = range.getBoundingClientRect();
        if (!anchor || (!rect.width && !rect.height)) return setSelection(null);
        const coarse = window.matchMedia?.("(pointer: coarse)").matches;
        setSelection({
          anchor,
          x: Math.min(Math.max(rect.left + rect.width / 2, 60), window.innerWidth - 60),
          y: coarse ? Math.min(rect.bottom + 12, window.innerHeight - 56) : Math.max(rect.top - 46, 8),
        });
      }, 120);
    };
    document.addEventListener("selectionchange", update);
    window.addEventListener("scroll", update, true);
    return () => {
      window.clearTimeout(timer);
      document.removeEventListener("selectionchange", update);
      window.removeEventListener("scroll", update, true);
    };
  }, [root]);

  // Clicking highlighted text opens its thread.
  useEffect(() => {
    const container = root.current;
    if (!container) return;
    const click = (event: MouseEvent) => {
      if (!index.current || !window.getSelection()?.isCollapsed) return;
      if ((event.target as Element | null)?.closest("button, a, input, textarea, .comment-layer")) return;
      const offset = caretOffset(index.current, event.clientX, event.clientY);
      if (offset === null) return;
      const hits = anchored.filter((item) => item.thread.status === "open" && offset >= item.start && offset < item.end);
      if (!hits.length) return;
      setActive(hits[0].thread.id);
      setDrawer({ focus: hits.map((item) => item.thread.id), compose: null });
    };
    container.addEventListener("click", click);
    return () => container.removeEventListener("click", click);
  }, [anchored, root, setDrawer]);

  const reveal = (thread: CommentThread) => {
    const item = anchored.find((entry) => entry.thread.id === thread.id);
    if (!item) return;
    setActive(thread.id);
    const start = item.range.startContainer;
    (start instanceof Element ? start : start.parentElement)?.scrollIntoView({ block: "center", behavior: "smooth" });
  };

  const listed = (() => {
    const visible = showResolved ? threads : threads.filter((thread) => thread.status === "open");
    const focused = drawer?.focus ? visible.filter((thread) => drawer.focus!.includes(thread.id)) : visible;
    return [...focused].sort((a, b) => {
      const ai = anchored.find((item) => item.thread.id === a.id)?.start ?? Number.MAX_SAFE_INTEGER;
      const bi = anchored.find((item) => item.thread.id === b.id)?.start ?? Number.MAX_SAFE_INTEGER;
      return ai - bi || a.createdAt.localeCompare(b.createdAt);
    });
  })();

  return (
    <div className="comment-layer" data-comment-ignore>
      {bubbles.map((bubble) => (
        <button
          key={bubble.key}
          type="button"
          className="comment-bubble"
          style={{ top: bubble.top, left: bubble.left }}
          aria-label={`${bubble.threads.length} comment${bubble.threads.length === 1 ? "" : "s"} on this paragraph`}
          onClick={() => {
            setActive(bubble.threads[0].id);
            setDrawer({ focus: bubble.threads.map((thread) => thread.id), compose: null });
          }}
        >
          {bubble.threads.length}
          {bubble.threads.some((thread) => !thread.awaitingReply) && <span className="bubble-answered" aria-hidden />}
        </button>
      ))}
      {selection && !drawer?.compose && (
        <button
          type="button"
          className="selection-comment-button"
          style={{ left: selection.x, top: selection.y }}
          // Keep the text selected while the button is pressed.
          onPointerDown={(event) => event.preventDefault()}
          onClick={() => {
            setDrawer({ focus: [], compose: { anchor: selection.anchor } });
            setSelection(null);
          }}
        >
          <MessageSquarePlus size={15} /> Ask about this
        </button>
      )}
      {drawer && (
        <div className="comment-drawer" role="dialog" aria-modal="false" aria-label="Passage comments">
          <header className="comment-drawer-header">
            <h2>{drawer.compose ? "Ask about a passage" : "Passage comments"}</h2>
            <button type="button" className="button button-ghost button-icon" aria-label="Close comments" onClick={() => setDrawer(null)}>
              <X size={16} />
            </button>
          </header>
          <div className="comment-drawer-body scroll-area">
            {drawer.compose && (
              <div className="comment-compose-passage">
                <blockquote className="comment-quote">{drawer.compose.anchor.exact}</blockquote>
                <Composer
                  autoFocus
                  placeholder="What would you like to know? Ask a question or leave a note…"
                  submitLabel="Comment"
                  busy={actions.create.isPending}
                  onCancel={() => setDrawer(null)}
                  onSubmit={async (body) => {
                    const thread = await actions.create.mutateAsync({ anchor: drawer.compose!.anchor, body });
                    window.getSelection()?.removeAllRanges();
                    setActive(thread.id);
                    setDrawer({ focus: [thread.id], compose: null });
                  }}
                />
              </div>
            )}
            {!drawer.compose && (
              <div className="comment-drawer-filters">
                {drawer.focus && (
                  <button type="button" className="discussion-link" onClick={() => setDrawer({ focus: null, compose: null })}>
                    Show all passages
                  </button>
                )}
                <label className="discussion-toggle">
                  <input type="checkbox" checked={showResolved} onChange={(event) => setShowResolved(event.target.checked)} />
                  Show resolved
                </label>
              </div>
            )}
            {!drawer.compose && listed.length === 0 && (
              <p className="muted discussion-empty">
                No passage comments{showResolved ? "" : " open"}. Select any text in the note and choose <strong>Ask about this</strong>.
              </p>
            )}
            {!drawer.compose &&
              listed.map((thread) => (
                <div key={thread.id} className={`drawer-thread ${active === thread.id ? "active" : ""}`}
                  onMouseEnter={() => thread.anchorState === "anchored" && setActive(thread.id)}>
                  <button type="button" className="drawer-thread-jump" onClick={() => reveal(thread)} disabled={thread.anchorState !== "anchored"}>
                    {thread.anchorState === "anchored" ? "Show in note" : "Passage no longer in note"}
                  </button>
                  <ThreadView thread={thread} actions={actions} showQuote />
                </div>
              ))}
          </div>
        </div>
      )}
    </div>
  );
}
