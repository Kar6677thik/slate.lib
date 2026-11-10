"use client";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useApi } from "@/lib/auth/context";
import type { CommentAnchor, CommentThread, NoteComments } from "@/lib/api/contracts";

export const commentsKey = (noteId: string) => ["comments", noteId] as const;

/**
 * One note's comment threads. Agents answer from another app (Claude, ChatGPT) while the note is open, so the query
 * refetches when the window regains focus and every minute while visible.
 */
export function useNoteComments(noteId: string) {
  const api = useApi();
  const cache = useQueryClient();
  const query = useQuery({
    queryKey: commentsKey(noteId),
    queryFn: ({ signal }) => api.comments(noteId, signal),
    refetchOnWindowFocus: true,
    refetchInterval: 60_000,
    staleTime: 10_000,
  });
  const put = (thread: CommentThread) =>
    cache.setQueryData<NoteComments>(commentsKey(noteId), (current) =>
      current
        ? {
            ...current,
            threads: current.threads.some((item) => item.id === thread.id)
              ? current.threads.map((item) => (item.id === thread.id ? thread : item))
              : [...current.threads, thread],
          }
        : current,
    );
  const settle = () => void cache.invalidateQueries({ queryKey: ["comment-inbox"] });
  const create = useMutation({
    mutationFn: ({ anchor, body }: { anchor: CommentAnchor; body: string }) => api.createComment(noteId, anchor, body),
    onSuccess: (thread) => { put(thread); settle(); },
  });
  const reply = useMutation({
    mutationFn: ({ threadId, body }: { threadId: string; body: string }) => api.replyToComment(noteId, threadId, body),
    onSuccess: (thread) => { put(thread); settle(); },
  });
  const status = useMutation({
    mutationFn: ({ threadId, status }: { threadId: string; status: "open" | "resolved" }) =>
      api.setCommentStatus(noteId, threadId, status),
    onSuccess: (thread) => { put(thread); settle(); },
  });
  const visibility = useMutation({
    mutationFn: ({ threadId, hidden }: { threadId: string; hidden: boolean }) => api.setCommentHidden(noteId, threadId, hidden),
    onSuccess: (thread) => { put(thread); settle(); },
  });
  const remove = useMutation({
    mutationFn: ({ threadId, commentId }: { threadId: string; commentId: string }) =>
      api.deleteComment(noteId, threadId, commentId),
    onSuccess: () => { void cache.invalidateQueries({ queryKey: commentsKey(noteId) }); settle(); },
  });
  return { query, create, reply, status, visibility, remove };
}

export type NoteCommentActions = ReturnType<typeof useNoteComments>;
