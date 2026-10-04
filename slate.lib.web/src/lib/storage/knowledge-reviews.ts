"use client";

import { useCallback, useSyncExternalStore } from "react";
import type { KnowledgeReview, ReviewState } from "@/lib/intelligence/knowledge-issues";

const eventName = "slate-knowledge-reviews";
const cache = new Map<string, { raw: string | null; value: Record<string, KnowledgeReview> }>();
const emptyReviews: Record<string, KnowledgeReview> = {};
const key = (scope: string) => `slate.knowledge-reviews.${scope}`;

export function readKnowledgeReviews(scope: string) {
  if (typeof localStorage === "undefined") return {} as Record<string, KnowledgeReview>;
  const raw = localStorage.getItem(key(scope));
  const saved = cache.get(scope);
  if (saved?.raw === raw) return saved.value;
  let value: Record<string, KnowledgeReview> = {};
  try {
    const parsed = raw ? JSON.parse(raw) as Record<string, KnowledgeReview> : {};
    value = Object.fromEntries(Object.entries(parsed).filter(([fingerprint, review]) => /^issue-[a-z0-9-]+$/.test(fingerprint) && ["open", "resolved", "dismissed", "snoozed"].includes(review?.state)).slice(-2000));
  } catch { value = {}; }
  cache.set(scope, { raw, value });
  return value;
}

function subscribe(scope: string, callback: () => void) {
  const listener = (event: Event) => { if (!(event instanceof CustomEvent) || event.detail === scope) callback(); };
  addEventListener(eventName, listener); addEventListener("storage", callback);
  return () => { removeEventListener(eventName, listener); removeEventListener("storage", callback); };
}

export function useKnowledgeReviews(scope: string) {
  const reviews = useSyncExternalStore(useCallback((callback) => subscribe(scope, callback), [scope]), useCallback(() => readKnowledgeReviews(scope), [scope]), () => emptyReviews);
  return {
    reviews,
    setReview(fingerprint: string, state: ReviewState, reason?: string) {
      const next = { ...readKnowledgeReviews(scope), [fingerprint]: { fingerprint, state, reason: reason?.slice(0, 300), reviewedAt: new Date().toISOString() } };
      const raw = JSON.stringify(next); localStorage.setItem(key(scope), raw); cache.set(scope, { raw, value: next });
      dispatchEvent(new CustomEvent(eventName, { detail: scope }));
    },
  };
}
