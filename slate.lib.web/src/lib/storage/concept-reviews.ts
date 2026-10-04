"use client";
import { useSyncExternalStore } from "react";
import { normalizeConcept, type ConceptIdentityReview } from "@/lib/intelligence/concepts";

const eventName = "slate-concept-reviews";
const cache = new Map<string, { raw: string; value: ConceptIdentityReview }>();
function key(scope: string) { return `slate.concept-reviews.v1.${scope}`; }
export function readConceptReviews(scope: string): ConceptIdentityReview {
  if (typeof localStorage === "undefined") return {};
  const raw = localStorage.getItem(key(scope)) ?? ""; const cached = cache.get(scope); if (cached?.raw === raw) return cached.value;
  let value: ConceptIdentityReview = {}; try { const parsed = JSON.parse(raw || "{}"); if (parsed && typeof parsed === "object") value = parsed; } catch {}
  cache.set(scope, { raw, value }); return value;
}
function write(scope: string, value: ConceptIdentityReview) { const raw = JSON.stringify(value); localStorage.setItem(key(scope), raw); cache.set(scope, { raw, value }); dispatchEvent(new CustomEvent(eventName, { detail: scope })); }
export function useConceptReviews(scope: string) {
  const reviews = useSyncExternalStore<ConceptIdentityReview>((notify) => { const listener = (event: Event) => { if ((event as CustomEvent).detail === scope) notify(); }; addEventListener(eventName, listener); return () => removeEventListener(eventName, listener); }, () => readConceptReviews(scope), () => ({}));
  return { reviews,
    merge(from: string, into: string) { write(scope, { ...reviews, merge: { ...(reviews.merge ?? {}), [normalizeConcept(from)]: into.trim() } }); },
    addAlias(concept: string, alias: string) { const id = normalizeConcept(concept); write(scope, { ...reviews, aliases: { ...(reviews.aliases ?? {}), [id]: [...new Set([...(reviews.aliases?.[id] ?? []), alias.trim()])].slice(0, 12) } }); },
    keepSeparate(a: string, b: string) { write(scope, { ...reviews, separate: [...(reviews.separate ?? []), [normalizeConcept(a), normalizeConcept(b)]].slice(-50) }); },
  };
}
