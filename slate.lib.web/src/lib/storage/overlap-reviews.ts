"use client";

import { useCallback, useSyncExternalStore } from "react";
import type { OverlapReviewState } from "@/lib/intelligence/overlap";

export interface OverlapReview { fingerprint: string; state: OverlapReviewState; reviewedAt: string; reason?: string; }
const eventName = "slate-overlap-reviews";
const cache = new Map<string, { raw: string | null; value: Record<string, OverlapReview> }>();
const empty: Record<string, OverlapReview> = {};
const key = (scope: string) => `slate.overlap-reviews.${scope}`;

export function readOverlapReviews(scope: string) {
  if (typeof localStorage === "undefined") return {} as Record<string, OverlapReview>;
  const raw = localStorage.getItem(key(scope)); const saved = cache.get(scope); if (saved?.raw === raw) return saved.value;
  let value: Record<string, OverlapReview> = {};
  try { const parsed = raw ? JSON.parse(raw) as Record<string, OverlapReview> : {}; value = Object.fromEntries(Object.entries(parsed).filter(([fingerprint, review]) => /^overlap-[a-z0-9-]+$/.test(fingerprint) && ["open", "keep-separate", "resolved", "dismissed"].includes(review?.state)).slice(-2000)); } catch { value = {}; }
  cache.set(scope, { raw, value }); return value;
}
function subscribe(scope: string, callback: () => void) { const listener = (event: Event) => { if (!(event instanceof CustomEvent) || event.detail === scope) callback(); }; addEventListener(eventName, listener); addEventListener("storage", callback); return () => { removeEventListener(eventName, listener); removeEventListener("storage", callback); }; }
export function useOverlapReviews(scope: string) {
  const reviews = useSyncExternalStore(useCallback((callback) => subscribe(scope, callback), [scope]), useCallback(() => readOverlapReviews(scope), [scope]), () => empty);
  return { reviews, setReview(fingerprint: string, state: OverlapReviewState, reason?: string) { const next = { ...readOverlapReviews(scope), [fingerprint]: { fingerprint, state, reason: reason?.slice(0, 300), reviewedAt: new Date().toISOString() } }; const raw = JSON.stringify(next); localStorage.setItem(key(scope), raw); cache.set(scope, { raw, value: next }); dispatchEvent(new CustomEvent(eventName, { detail: scope })); } };
}

export interface ManualMergeDraft { fingerprint: string; targetNoteId: string; sourceNoteId: string; markdown: string; createdAt: string; }
export function saveManualMergeDraft(scope: string, draft: ManualMergeDraft) { localStorage.setItem(`slate.overlap-merge-draft.${scope}.${draft.fingerprint}`, JSON.stringify(draft)); }
