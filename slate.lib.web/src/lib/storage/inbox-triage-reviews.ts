"use client";
import { useCallback, useSyncExternalStore } from "react";
import type { InboxReviewState, SuggestionReviewState } from "@/lib/intelligence/inbox-triage";

export interface InboxItemReview { state: InboxReviewState; captureHash: string; reviewedAt: string; }
export interface InboxSuggestionReview { state: SuggestionReviewState; reviewedAt: string; }
export interface InboxTriageReviews { items: Record<string, InboxItemReview>; suggestions: Record<string, InboxSuggestionReview>; }
const empty: InboxTriageReviews = { items: {}, suggestions: {} }, eventName = "slate-inbox-triage-reviews", cache = new Map<string,{raw:string;value:InboxTriageReviews}>();
const key = (scope: string) => `slate.inbox-triage-reviews.v1.${scope}`;
export function readInboxTriageReviews(scope: string): InboxTriageReviews {
  if (typeof localStorage === "undefined") return empty; const raw = localStorage.getItem(key(scope)) ?? ""; if (cache.get(scope)?.raw === raw) return cache.get(scope)!.value;
  let value = empty; try { const parsed = JSON.parse(raw || "{}") as Partial<InboxTriageReviews>; value = { items:Object.fromEntries(Object.entries(parsed.items ?? {}).filter(([,review]) => ["unprocessed","processed","deferred"].includes(review?.state)).slice(-2000)), suggestions:Object.fromEntries(Object.entries(parsed.suggestions ?? {}).filter(([,review]) => ["accepted","dismissed","not-relevant"].includes(review?.state)).slice(-4000)) }; } catch {}
  cache.set(scope,{raw,value}); return value;
}
function write(scope: string, value: InboxTriageReviews) { const bounded = { items:Object.fromEntries(Object.entries(value.items).slice(-2000)), suggestions:Object.fromEntries(Object.entries(value.suggestions).slice(-4000)) }; const raw=JSON.stringify(bounded); localStorage.setItem(key(scope),raw); cache.set(scope,{raw,value:bounded}); dispatchEvent(new CustomEvent(eventName,{detail:scope})); }
export function useInboxTriageReviews(scope: string) {
  const reviews = useSyncExternalStore(useCallback((notify) => { const listener=(event:Event) => { if (!(event instanceof CustomEvent) || event.detail === scope) notify(); }; addEventListener(eventName,listener); addEventListener("storage",notify); return () => { removeEventListener(eventName,listener); removeEventListener("storage",notify); }; },[scope]),useCallback(() => readInboxTriageReviews(scope),[scope]),() => empty);
  return { reviews,
    state(captureId:string,captureHash:string):InboxReviewState { const review=reviews.items[captureId]; return review?.captureHash === captureHash ? review.state : "unprocessed"; },
    setItem(captureId:string,captureHash:string,state:InboxReviewState) { write(scope,{...readInboxTriageReviews(scope),items:{...readInboxTriageReviews(scope).items,[captureId]:{state,captureHash,reviewedAt:new Date().toISOString()}}}); },
    setSuggestion(fingerprint:string,state:SuggestionReviewState) { write(scope,{...readInboxTriageReviews(scope),suggestions:{...readInboxTriageReviews(scope).suggestions,[fingerprint]:{state,reviewedAt:new Date().toISOString()}}}); }
  };
}
