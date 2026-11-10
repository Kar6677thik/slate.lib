"use client";
import { useEffect, useMemo, useRef, useState } from "react";
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { format, isToday, isYesterday } from "date-fns";
import { Bell, Bot, CheckCheck, FilePlus2, FileText, Inbox, MessageSquare, Smartphone } from "lucide-react";
import { useApi, useOptionalApi } from "@/lib/auth/context";
import { useWorkspace } from "@/features/notes/workspace-context";
import { requestThreadFocus } from "@/features/notes/thread-focus";
import type { NotificationItem, SlateNotification } from "@/lib/api/contracts";
import { when } from "@/components/comments/comment-thread";
import { Button } from "@/components/ui/button";
import { ErrorMessage, Loading } from "@/components/common/primitives";

export const notificationSummaryKey = ["notification-summary"] as const;

/** Unread count for the navigation badge. Quietly polled; never shown as a pop-up. */
export function useUnreadNotifications() {
  const api = useOptionalApi();
  const query = useQuery({
    queryKey: notificationSummaryKey,
    queryFn: ({ signal }) => api!.notificationSummary(signal),
    enabled: !!api,
    refetchInterval: 60_000,
    refetchOnWindowFocus: true,
    staleTime: 15_000,
    retry: false,
  });
  return query.data?.unread ?? 0;
}

const sourceVerb: Record<string, string> = {
  created: "created",
  captured: "captured",
  copied: "copied",
  duplicated: "duplicated",
  restored: "restored",
};

function dayLabel(value: string) {
  const date = new Date(value);
  if (isToday(date)) return "Today";
  if (isYesterday(date)) return "Yesterday";
  return format(date, "EEEE, d MMMM yyyy");
}

/** Markdown punctuation is noise in a one-line preview. */
function plainExcerpt(value: string) {
  return value.replace(/\[\[(?:id:[0-9a-f-]+\|)?([^\]]+)\]\]/gi, "$1").replace(/[*_`#>]+/g, "").replace(/\s+/g, " ").trim();
}

function ActorIcon({ notification }: { notification: SlateNotification }) {
  if (notification.actor.kind === "agent") return <span className="notification-avatar agent"><Bot size={17} /></span>;
  if (notification.kind === "notes")
    return (
      <span className="notification-avatar">
        {notification.notes?.source === "captured" ? <Inbox size={16} /> : <Smartphone size={16} />}
      </span>
    );
  return <span className="notification-avatar">{notification.actor.name.trim()[0]?.toUpperCase() ?? "?"}</span>;
}

function CommentEntry({ item, onOpen }: { item: NotificationItem; onOpen: () => void }) {
  const w = useWorkspace();
  const n = item.notification;
  const c = n.comment!;
  return (
    <>
      <button type="button" className="notification-main" onClick={onOpen}>
        <span className="notification-text">
          <strong>{n.actor.name}</strong>{" "}
          {c.isReply ? "replied on" : "commented on"} <strong>{c.noteTitle}</strong>
          {c.anchorKind === "text" ? "" : " (discussion)"}
        </span>
        {c.quote && <span className="notification-quote">{c.quote}</span>}
        <span className="notification-excerpt">{plainExcerpt(c.excerpt) || "Attached a page"}</span>
      </button>
      {c.linkedNote && (
        <button type="button" className="notification-page" onClick={() => w.open(c.linkedNote!.noteId)}>
          <FileText size={15} />
          <span>
            <small>Wrote a page for this</small>
            <strong>{c.linkedNote.title ?? "Open page"}</strong>
          </span>
        </button>
      )}
    </>
  );
}

function NotesEntry({ item, onOpen }: { item: NotificationItem; onOpen: (noteId: string) => void }) {
  const n = item.notification;
  const notes = n.notes!;
  const [expanded, setExpanded] = useState(notes.notes.length <= 5);
  const shown = expanded ? notes.notes : notes.notes.slice(0, 5);
  const verb = sourceVerb[notes.source] ?? notes.source;
  const inInbox = notes.notes.every((note) => /^inbox\//i.test(note.path));
  return (
    <div className="notification-main static">
      <span className="notification-text">
        <strong>{n.actor.name}</strong> {verb}{" "}
        {notes.count === 1 ? "a page" : `${notes.count.toLocaleString()} pages`}
        {inInbox ? " in Inbox" : ""}
      </span>
      <ul className="notification-notes">
        {shown.map((note) => (
          <li key={note.noteId}>
            <button type="button" onClick={() => onOpen(note.noteId)} title={note.path}>
              <FileText size={14} />
              <span>{note.title}</span>
              <small>{note.path.includes("/") ? note.path.slice(0, note.path.lastIndexOf("/")) : "Library"}</small>
            </button>
          </li>
        ))}
      </ul>
      {!expanded && (
        <button type="button" className="show-replies" onClick={() => setExpanded(true)}>
          Show all {notes.notes.length}
        </button>
      )}
      {expanded && notes.count > notes.notes.length && (
        <p className="muted notification-more">
          …and {(notes.count - notes.notes.length).toLocaleString()} more. Search or browse the library to see the rest.
        </p>
      )}
    </div>
  );
}

/**
 * A history of what happened in the library while you were elsewhere: every agent or other-device comment, and new
 * pages bundled per burst. Opening the page marks everything read; entries that were new stay marked until you leave.
 */
export function NotificationsPage() {
  const api = useApi();
  const w = useWorkspace();
  const cache = useQueryClient();
  const [kind, setKind] = useState<"" | "comment" | "notes">("");
  const [includeOwn, setIncludeOwn] = useState(false);
  const query = useInfiniteQuery({
    queryKey: ["notifications", kind, includeOwn],
    queryFn: ({ pageParam, signal }) => api.notifications(pageParam, kind, includeOwn, signal),
    initialPageParam: 0,
    getNextPageParam: (last) => last.nextPage ?? undefined,
    refetchOnWindowFocus: true,
    refetchInterval: 60_000,
  });
  const items = useMemo(() => query.data?.pages.flatMap((page) => page.items) ?? [], [query.data]);
  // Remember what was new when the page was opened, so marking read does not erase the "new" markers.
  const fresh = useRef(new Set<string>());
  for (const item of items) if (item.unread) fresh.current.add(item.notification.id);
  const markRead = useMutation({
    mutationFn: () => api.markNotificationsRead({ all: true }),
    onSuccess: (summary) => cache.setQueryData(notificationSummaryKey, summary),
  });
  const unread = query.data?.pages[0]?.unread ?? 0;
  const { mutate } = markRead;
  useEffect(() => {
    if (unread === 0) return;
    const timer = window.setTimeout(() => mutate(), 1500);
    return () => window.clearTimeout(timer);
  }, [unread, mutate]);

  const openThread = (n: SlateNotification) => {
    requestThreadFocus(n.comment!.noteId, n.comment!.threadId);
    w.open(n.comment!.noteId);
  };

  const groups: { label: string; items: NotificationItem[] }[] = [];
  for (const item of items) {
    const label = dayLabel(item.notification.updatedAt);
    if (groups.at(-1)?.label !== label) groups.push({ label, items: [] });
    groups.at(-1)!.items.push(item);
  }

  return (
    <div className="view-body scroll-area notifications-view">
      <header className="notifications-header">
        <div>
          <h1>Notifications</h1>
          <p className="muted">Replies to your comments and pages added from other devices or by your AI assistant.</p>
        </div>
        <Button variant="outline" size="sm" disabled={markRead.isPending || unread === 0} onClick={() => mutate()}>
          <CheckCheck size={15} /> Mark all read
        </Button>
      </header>
      <div className="notifications-filters">
        <div className="notifications-tabs" role="tablist" aria-label="Notification type">
          {([["", "All", Bell], ["comment", "Comments", MessageSquare], ["notes", "New pages", FilePlus2]] as const).map(([value, label, Icon]) => (
            <button key={label} type="button" role="tab" aria-selected={kind === value} className={kind === value ? "active" : ""} onClick={() => setKind(value)}>
              <Icon size={14} /> {label}
            </button>
          ))}
        </div>
        <label className="discussion-toggle">
          <input type="checkbox" checked={includeOwn} onChange={(event) => setIncludeOwn(event.target.checked)} />
          Include this device
        </label>
      </div>
      {query.isPending ? (
        <Loading label="Loading notifications…" />
      ) : query.error ? (
        <ErrorMessage error={query.error} retry={() => query.refetch()} />
      ) : items.length === 0 ? (
        <div className="notifications-empty">
          <Bell size={28} />
          <p>Nothing yet. When your AI assistant answers a question or a page arrives from another device, it shows up here.</p>
        </div>
      ) : (
        <div className="notifications-list">
          {groups.map((group) => (
            <section key={group.label} aria-label={group.label}>
              <h2 className="notifications-day">{group.label}</h2>
              <ol>
                {group.items.map((item) => (
                  <li key={item.notification.id} className={`notification ${fresh.current.has(item.notification.id) ? "unread" : ""}`}>
                    <ActorIcon notification={item.notification} />
                    <div className="notification-body">
                      {item.notification.kind === "comment" && item.notification.comment ? (
                        <CommentEntry item={item} onOpen={() => openThread(item.notification)} />
                      ) : item.notification.notes ? (
                        <NotesEntry item={item} onOpen={(noteId) => w.open(noteId)} />
                      ) : null}
                      <time dateTime={item.notification.updatedAt} title={new Date(item.notification.updatedAt).toLocaleString()}>
                        {when(item.notification.updatedAt)}
                        {item.fromThisDevice ? " · this device" : ""}
                      </time>
                    </div>
                    {fresh.current.has(item.notification.id) && (
                      <span className="notification-dot">
                        <span className="sr-only">New</span>
                      </span>
                    )}
                  </li>
                ))}
              </ol>
            </section>
          ))}
          {query.hasNextPage && (
            <Button variant="outline" size="sm" disabled={query.isFetchingNextPage} onClick={() => query.fetchNextPage()}>
              {query.isFetchingNextPage ? "Loading…" : "Load older notifications"}
            </Button>
          )}
        </div>
      )}
    </div>
  );
}
