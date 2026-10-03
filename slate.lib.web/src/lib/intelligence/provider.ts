import { createHash } from "node:crypto";
import type { EmbeddingProvider } from "./types";

export class DisabledEmbeddingProvider implements EmbeddingProvider {
  readonly name = "disabled";
  readonly model = "none";
  readonly dimensions = 0;
  readonly available = false;
  async embedQuery(): Promise<number[]> { throw new Error("Semantic search is disabled"); }
  async embedDocuments(): Promise<number[][]> { throw new Error("Semantic search is disabled"); }
}

export class DeterministicEmbeddingProvider implements EmbeddingProvider {
  readonly name = "deterministic-test";
  readonly model = "sha256-v1";
  readonly available = true;
  constructor(readonly dimensions = 32) {}
  private embed(text: string) {
    const words = text.toLowerCase().match(/[\p{L}\p{N}_-]+/gu) ?? [];
    const vector = Array.from({ length: this.dimensions }, () => 0);
    for (const word of words) {
      const hash = createHash("sha256").update(word).digest();
      for (let i = 0; i < this.dimensions; i++) vector[i] += (hash[i % hash.length] - 127.5) / 127.5;
    }
    const norm = Math.hypot(...vector) || 1;
    return vector.map((value) => value / norm);
  }
  async embedQuery(text: string) { return this.embed(text); }
  async embedDocuments(texts: string[]) { return texts.map((text) => this.embed(text)); }
}

export class OpenAICompatibleEmbeddingProvider implements EmbeddingProvider {
  readonly name = "openai-compatible";
  readonly available = true;
  constructor(
    readonly model: string,
    readonly dimensions: number,
    private readonly endpoint: URL,
    private readonly apiKey: string,
  ) {}
  private async responseJson(response: Response) {
    if (!response.body) throw new Error("Embedding provider returned an empty response");
    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let text = "";
    let received = 0;
    try {
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        received += value.byteLength;
        if (received > 4 * 1024 * 1024) {
          await reader.cancel();
          throw new Error("Embedding provider response is too large");
        }
        text += decoder.decode(value, { stream: true });
      }
      text += decoder.decode();
      return JSON.parse(text) as { data?: { index: number; embedding: number[] }[] };
    } finally {
      reader.releaseLock();
    }
  }
  private async embed(input: string[], signal?: AbortSignal) {
    const response = await fetch(this.endpoint, {
      method: "POST",
      signal: signal ? AbortSignal.any([signal, AbortSignal.timeout(30_000)]) : AbortSignal.timeout(30_000),
      redirect: "error",
      headers: { "content-type": "application/json", authorization: `Bearer ${this.apiKey}` },
      body: JSON.stringify({ model: this.model, input, dimensions: this.dimensions }),
    });
    if (!response.ok) throw new Error(`Embedding provider returned ${response.status}`);
    if (Number(response.headers.get("content-length") ?? 0) > 4 * 1024 * 1024) throw new Error("Embedding provider response is too large");
    const json = await this.responseJson(response);
    const vectors = [...(json.data ?? [])].sort((a, b) => a.index - b.index).map((item) => item.embedding);
    if (vectors.length !== input.length || vectors.some((v) => !Array.isArray(v) || v.length !== this.dimensions || v.some((value) => typeof value !== "number" || !Number.isFinite(value)))) throw new Error("Embedding provider returned an invalid response");
    return vectors;
  }
  async embedQuery(text: string, signal?: AbortSignal) { return (await this.embed([text], signal))[0]; }
  embedDocuments(texts: string[], signal?: AbortSignal) { return this.embed(texts, signal); }
}

export function getEmbeddingProvider(): EmbeddingProvider {
  const kind = (process.env.SLATE_EMBEDDING_PROVIDER ?? "disabled").toLowerCase();
  if (kind === "fake" && process.env.NODE_ENV !== "production") return new DeterministicEmbeddingProvider(Number(process.env.SLATE_EMBEDDING_DIMENSIONS ?? 32));
  if (kind !== "openai-compatible") return new DisabledEmbeddingProvider();
  const raw = process.env.SLATE_EMBEDDING_URL;
  const key = process.env.SLATE_EMBEDDING_API_KEY;
  if (!raw || !key) return new DisabledEmbeddingProvider();
  const endpoint = new URL(raw);
  const allowed = new Set((process.env.SLATE_EMBEDDING_ALLOWED_ORIGINS ?? "").split(",").map((v) => v.trim()).filter(Boolean));
  const loopback = ["localhost", "127.0.0.1", "[::1]"].includes(endpoint.hostname);
  if (endpoint.username || endpoint.password || endpoint.search || endpoint.hash || (!loopback && endpoint.protocol !== "https:") || (!loopback && !allowed.has(endpoint.origin))) return new DisabledEmbeddingProvider();
  const dimensions = Number(process.env.SLATE_EMBEDDING_DIMENSIONS ?? 1536);
  if (!Number.isInteger(dimensions) || dimensions < 1 || dimensions > 2000) return new DisabledEmbeddingProvider();
  return new OpenAICompatibleEmbeddingProvider(process.env.SLATE_EMBEDDING_MODEL ?? "text-embedding-3-small", dimensions, endpoint, key);
}
