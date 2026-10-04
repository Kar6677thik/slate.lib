"use client";
import { useCallback, useSyncExternalStore } from "react";
import type { HealthReviewState } from "@/lib/intelligence/health";
export interface HealthOnlyReview { state: HealthReviewState; reviewedAt: string; }
const eventName = "slate-health-reviews"; const cache = new Map<string, { raw: string; value: Record<string, HealthOnlyReview> }>();
const emptyReviews: Record<string, HealthOnlyReview> = {};
const key = (scope: string) => `slate.health-reviews.v1.${scope}`;
export function readHealthReviews(scope: string) {
  if (typeof localStorage === "undefined") return {} as Record<string, HealthOnlyReview>; const raw = localStorage.getItem(key(scope)) ?? ""; const previous = cache.get(scope); if (previous?.raw === raw) return previous.value;
  let value: Record<string, HealthOnlyReview> = {}; try { const parsed = JSON.parse(raw || "{}") as Record<string, HealthOnlyReview>; value = Object.fromEntries(Object.entries(parsed).filter(([, review]) => ["open", "resolved", "dismissed", "not-relevant"].includes(review?.state)).slice(-4000)); } catch {}
  cache.set(scope, { raw, value }); return value;
}
export function useHealthReviews(scope: string) {
  const reviews = useSyncExternalStore(useCallback((notify) => { const handler = (event: Event) => { if ((event as CustomEvent).detail === scope) notify(); }; addEventListener(eventName, handler); addEventListener("storage", notify); return () => { removeEventListener(eventName, handler); removeEventListener("storage", notify); }; }, [scope]), useCallback(() => readHealthReviews(scope), [scope]), () => emptyReviews);
  return { reviews, setReview(fingerprint: string, state: HealthReviewState) { const next = { ...readHealthReviews(scope), [fingerprint]: { state, reviewedAt: new Date().toISOString() } }; const raw = JSON.stringify(next); localStorage.setItem(key(scope), raw); cache.set(scope, { raw, value: next }); dispatchEvent(new CustomEvent(eventName, { detail: scope })); } };
}
