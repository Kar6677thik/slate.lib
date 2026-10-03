import { getGenerationProvider } from "./generation-provider";
import type { GenerationUsage } from "./ask-types";

const provider = getGenerationProvider();
const activeByLibrary = new Map<string, number>();
const statsByLibrary = new Map<string, { requests: number; retrievedChunks: number; inputTokens?: number; outputTokens?: number }>();
const configuredConcurrency = Number(process.env.SLATE_GENERATION_MAX_CONCURRENCY ?? 2);
const maximumConcurrent = Number.isInteger(configuredConcurrency) && configuredConcurrency >= 1 && configuredConcurrency <= 4 ? configuredConcurrency : 2;

export function generationProvider() { return provider; }

export function acquireGeneration(libraryId: string) {
  const active = activeByLibrary.get(libraryId) ?? 0;
  if (active >= maximumConcurrent) return null;
  activeByLibrary.set(libraryId, active + 1);
  const current = statsByLibrary.get(libraryId) ?? { requests: 0, retrievedChunks: 0 };
  current.requests += 1;
  statsByLibrary.set(libraryId, current);
  let released = false;
  return () => {
    if (released) return;
    released = true;
    activeByLibrary.set(libraryId, Math.max(0, (activeByLibrary.get(libraryId) ?? 1) - 1));
  };
}

export function recordAsk(libraryId: string, retrievedChunks: number, usage?: GenerationUsage) {
  const current = statsByLibrary.get(libraryId) ?? { requests: 0, retrievedChunks: 0 };
  current.retrievedChunks += retrievedChunks;
  if (typeof usage?.inputTokens === "number") current.inputTokens = (current.inputTokens ?? 0) + usage.inputTokens;
  if (typeof usage?.outputTokens === "number") current.outputTokens = (current.outputTokens ?? 0) + usage.outputTokens;
  statsByLibrary.set(libraryId, current);
}

export function generationStatus(libraryId: string) {
  return {
    askEnabled: provider.available,
    generationProvider: provider.name,
    generationModel: provider.model,
    askDefaultPolicy: "strict" as const,
    askRequestsThisRun: statsByLibrary.get(libraryId)?.requests ?? 0,
    askRetrievedChunksThisRun: statsByLibrary.get(libraryId)?.retrievedChunks ?? 0,
    askInputTokensThisRun: statsByLibrary.get(libraryId)?.inputTokens,
    askOutputTokensThisRun: statsByLibrary.get(libraryId)?.outputTokens,
    askActiveRequests: activeByLibrary.get(libraryId) ?? 0,
    askMaxConcurrent: maximumConcurrent,
  };
}
