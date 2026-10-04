import type { AssetPage, FolderPage, IntelligenceStatus, LinkIssuePage, Links, Note } from "@/lib/api/contracts";
import { aggregateLibraryHealth, HEALTH_LIMITS, type HealthFilters, type HealthReviewInput, type HealthSources } from "./health";
import { PostgresDerivedStore } from "./postgres-store";

export interface HealthCanonical {
  list(path: string, page: number, signal: AbortSignal): Promise<FolderPage>;
  note(id: string, signal: AbortSignal): Promise<Note>;
  links(id: string, signal: AbortSignal): Promise<Links>;
  linkIssues(page: number, signal: AbortSignal): Promise<LinkIssuePage>;
  assets(page: number, signal: AbortSignal): Promise<AssetPage>;
}
const store = new PostgresDerivedStore();
const cache = new Map<string, { expires: number; sources: HealthSources }>();

async function mapLimited<T, R>(values: T[], concurrency: number, operation: (value: T) => Promise<R>) {
  const results: R[] = new Array(values.length); let cursor = 0;
  await Promise.all(Array.from({ length: Math.min(concurrency, values.length) }, async () => { while (cursor < values.length) { const index = cursor++; results[index] = await operation(values[index]); } }));
  return results;
}
async function structuralSources(canonical: HealthCanonical, signal: AbortSignal) {
  const folders = [""]; const noteIds: string[] = []; let foldersRead = 0; let pagesRead = 0;
  while (folders.length && noteIds.length < HEALTH_LIMITS.maxNotes && foldersRead < HEALTH_LIMITS.maxFolders && pagesRead < HEALTH_LIMITS.maxFolderPages) {
    const path = folders.shift()!; let page = 0;
    do {
      const listing = await canonical.list(path, page, signal); pagesRead++;
      for (const entry of listing.entries) {
        if (entry.isDirectory && folders.length + foldersRead < HEALTH_LIMITS.maxFolders) folders.push(entry.path);
        else if (entry.id && noteIds.length < HEALTH_LIMITS.maxNotes) noteIds.push(entry.id);
      }
      if (listing.nextPage === null) break; page = listing.nextPage;
    } while (noteIds.length < HEALTH_LIMITS.maxNotes && pagesRead < HEALTH_LIMITS.maxFolderPages);
    foldersRead++;
  }
  const notes = await mapLimited(noteIds, HEALTH_LIMITS.sourceConcurrency, (id) => canonical.note(id, signal));
  const links = await mapLimited(notes, HEALTH_LIMITS.sourceConcurrency, (note) => canonical.links(note.id, signal).catch(() => null));
  return notes.flatMap((note, index) => links[index] ? [{ note, links: { ...links[index], outgoing: links[index].outgoing.slice(0, HEALTH_LIMITS.maxLinksPerNote), backlinks: links[index].backlinks.slice(0, HEALTH_LIMITS.maxLinksPerNote) } }] : []);
}
async function linkIssues(canonical: HealthCanonical, signal: AbortSignal) {
  const results = []; let page = 0;
  while (results.length < HEALTH_LIMITS.maxLinkIssues) { const response = await canonical.linkIssues(page, signal); results.push(...response.results); if (results.length >= response.total || !response.results.length) break; page++; }
  return results.slice(0, HEALTH_LIMITS.maxLinkIssues);
}
async function assets(canonical: HealthCanonical, signal: AbortSignal) {
  const results = []; let page = 0; let total = 0;
  while (results.length < HEALTH_LIMITS.maxAssets) { const response = await canonical.assets(page, signal); total = response.total; results.push(...response.results); if (results.length >= response.total || !response.results.length) break; page++; }
  return { values: results.slice(0, HEALTH_LIMITS.maxAssets), complete: results.length >= total };
}

export async function libraryHealth(libraryId: string, canonical: HealthCanonical, intelligence: IntelligenceStatus, filters: HealthFilters, reviews: HealthReviewInput, signal: AbortSignal, refresh = false) {
  const started = Date.now(); let sources = !refresh ? cache.get(libraryId)?.sources : undefined;
  if (!sources || (cache.get(libraryId)?.expires ?? 0) <= Date.now()) {
    const settled = await Promise.allSettled([store.readHealthDerived(libraryId), structuralSources(canonical, signal), linkIssues(canonical, signal), assets(canonical, signal)]);
    const failedSources: string[] = []; const derived: HealthSources = settled[0].status === "fulfilled" ? settled[0].value : (failedSources.push("derived intelligence"), {}); const notes = settled[1].status === "fulfilled" ? settled[1].value : (failedSources.push("note structure"), []); const links = settled[2].status === "fulfilled" ? settled[2].value : (failedSources.push("link diagnostics"), []); const assetResult = settled[3].status === "fulfilled" ? settled[3].value : (failedSources.push("asset metadata"), { values: [], complete: false });
    sources = { ...derived, libraryId, notes, linkIssues: links, assets: assetResult.values, assetInventoryComplete: assetResult.complete, intelligence, failedSources: [...new Set([...failedSources, ...(derived.failedSources ?? [])])], generatedAt: new Date().toISOString() };
    cache.set(libraryId, { sources, expires: Date.now() + HEALTH_LIMITS.cacheMs });
  } else sources = { ...sources, intelligence };
  return aggregateLibraryHealth(sources, filters, reviews, started);
}

export function invalidateLibraryHealth(libraryId: string) { cache.delete(libraryId); }
