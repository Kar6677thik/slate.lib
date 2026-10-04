"use client";
import { useSyncExternalStore } from "react";
import type { LinkSuggestionStatus } from "@/lib/intelligence/smart-links";
export interface LinkReview { fingerprint: string; status: LinkSuggestionStatus; reviewedAt: string; }
const event = "slate-link-reviews"; const cache = new Map<string, { raw: string; value: Record<string, LinkReview> }>();
function key(scope: string) { return `slate.link-reviews.v1.${scope}`; }
export function readLinkReviews(scope: string) {
  if (typeof localStorage === "undefined") return {}; const raw = localStorage.getItem(key(scope)) ?? ""; const cached = cache.get(scope); if (cached?.raw === raw) return cached.value;
  let value: Record<string, LinkReview> = {}; try { const parsed = JSON.parse(raw || "{}") as Record<string, LinkReview>; value = Object.fromEntries(Object.entries(parsed).filter(([fingerprint, review]) => /^link-[a-z0-9-]+$/.test(fingerprint) && ["open", "inserted", "dismissed", "not-relevant"].includes(review?.status)).slice(-3000)); } catch {}
  cache.set(scope, { raw, value }); return value;
}
export function useLinkReviews(scope: string) {
  const reviews = useSyncExternalStore((notify) => { const listener = (e: Event) => { if ((e as CustomEvent).detail === scope) notify(); }; addEventListener(event, listener); return () => removeEventListener(event, listener); }, () => readLinkReviews(scope), () => ({}));
  return { reviews, setReview(fingerprint: string, status: LinkSuggestionStatus) { const next = { ...readLinkReviews(scope), [fingerprint]: { fingerprint, status, reviewedAt: new Date().toISOString() } }; const raw = JSON.stringify(next); localStorage.setItem(key(scope), raw); cache.set(scope, { raw, value: next }); dispatchEvent(new CustomEvent(event, { detail: scope })); } };
}
