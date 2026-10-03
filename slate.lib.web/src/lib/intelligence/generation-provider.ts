import type { GenerationInput, GenerationProvider, GenerationUsage } from "./ask-types";

export class DisabledGenerationProvider implements GenerationProvider {
  readonly name = "disabled";
  readonly model = "none";
  readonly available = false;
  readonly maxInputTokens = 0;
  async generate(): Promise<{ text: string }> { throw new Error("Ask Slate is disabled"); }
  async *streamGenerate(): AsyncIterable<{ text?: string }> { throw new Error("Ask Slate is disabled"); }
}

export class DeterministicGenerationProvider implements GenerationProvider {
  readonly name = "deterministic-test";
  readonly model = "grounded-v1";
  readonly available = true;
  readonly maxInputTokens = 8_000;
  async generate(input: GenerationInput) {
    const ids = [...input.prompt.matchAll(/<SOURCE id="(S\d+)">/g)].map((match) => match[1]);
    const general = input.system.includes("General context");
    const text = ids.length
      ? `${general ? "## From your library\n\n" : ""}Your Slate notes contain relevant evidence for this question. [${ids[0]}]${ids[1] ? ` Supporting context also appears in [${ids[1]}].` : ""}${general ? "\n\n## General context\n\nNo additional general context is needed for this deterministic response." : ""}`
      : general
        ? "## From your library\n\nI couldn't find enough in your Slate library to answer that.\n\n## General context\n\nThe deterministic test provider does not add outside knowledge."
        : "I couldn't find enough in your Slate library to answer that.";
    return { text, usage: { inputTokens: Math.ceil((input.system.length + input.prompt.length) / 4), outputTokens: Math.ceil(text.length / 4) } };
  }
  async *streamGenerate(input: GenerationInput, signal?: AbortSignal) {
    const result = await this.generate(input);
    for (const part of result.text.match(/.{1,24}(?:\s|$)/g) ?? [result.text]) {
      if (signal?.aborted) throw signal.reason ?? new DOMException("Aborted", "AbortError");
      yield { text: part };
      await new Promise<void>((resolve) => setTimeout(resolve, 4));
    }
    yield { usage: result.usage };
  }
}

export class OpenAICompatibleGenerationProvider implements GenerationProvider {
  readonly name = "openai-compatible";
  readonly available = true;
  constructor(
    readonly model: string,
    readonly maxInputTokens: number,
    private readonly endpoint: URL,
    private readonly apiKey: string,
  ) {}
  private body(input: GenerationInput, stream: boolean) {
    return JSON.stringify({
      model: this.model,
      stream,
      max_tokens: input.maxOutputTokens,
      messages: [{ role: "system", content: input.system }, { role: "user", content: input.prompt }],
      temperature: 0.1,
    });
  }
  private async request(input: GenerationInput, stream: boolean, signal?: AbortSignal) {
    return fetch(this.endpoint, {
      method: "POST",
      redirect: "error",
      signal: AbortSignal.any([signal ?? new AbortController().signal, AbortSignal.timeout(60_000)]),
      headers: { "content-type": "application/json", authorization: `Bearer ${this.apiKey}` },
      body: this.body(input, stream),
    });
  }
  async generate(input: GenerationInput, signal?: AbortSignal) {
    const response = await this.request(input, false, signal);
    if (!response.ok) throw new Error(`Generation provider returned ${response.status}`);
    if (Number(response.headers.get("content-length") ?? 0) > 2 * 1024 * 1024) throw new Error("Generation response is too large");
    if (!response.headers.get("content-type")?.toLowerCase().includes("application/json")) throw new Error("Generation provider returned an invalid content type");
    const reader = response.body?.getReader();
    if (!reader) throw new Error("Generation provider returned an empty response");
    const chunks: Uint8Array[] = [];
    let received = 0;
    try {
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        received += value.byteLength;
        if (received > 2 * 1024 * 1024) throw new Error("Generation response is too large");
        chunks.push(value);
      }
    } finally { reader.releaseLock(); }
    const bytes = new Uint8Array(received);
    let offset = 0;
    for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
    let json: { choices?: { message?: { content?: unknown } }[]; usage?: { prompt_tokens?: number; completion_tokens?: number } };
    try { json = JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes)); }
    catch { throw new Error("Generation provider returned invalid JSON"); }
    const text = json.choices?.[0]?.message?.content;
    if (typeof text !== "string" || text.length > 48_000) throw new Error("Generation provider returned an invalid response");
    return { text, usage: { inputTokens: json.usage?.prompt_tokens, outputTokens: json.usage?.completion_tokens } };
  }
  async *streamGenerate(input: GenerationInput, signal?: AbortSignal) {
    const response = await this.request(input, true, signal);
    if (!response.ok || !response.body) throw new Error(`Generation provider returned ${response.status}`);
    if (!response.headers.get("content-type")?.toLowerCase().includes("text/event-stream")) throw new Error("Generation provider returned an invalid stream content type");
    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = "";
    let received = 0;
    let usage: GenerationUsage | undefined;
    try {
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        received += value.byteLength;
        if (received > 2 * 1024 * 1024) throw new Error("Generation response is too large");
        buffer += decoder.decode(value, { stream: true });
        const lines = buffer.split(/\r?\n/);
        buffer = lines.pop() ?? "";
        for (const line of lines) {
          if (!line.startsWith("data:")) continue;
          const data = line.slice(5).trim();
          if (!data || data === "[DONE]") continue;
          const json = JSON.parse(data) as { choices?: { delta?: { content?: unknown } }[]; usage?: { prompt_tokens?: number; completion_tokens?: number } };
          const text = json.choices?.[0]?.delta?.content;
          if (text !== undefined && typeof text !== "string") throw new Error("Invalid generation stream");
          if (typeof text === "string" && text) yield { text };
          if (json.usage) usage = { inputTokens: json.usage.prompt_tokens, outputTokens: json.usage.completion_tokens };
        }
      }
      if (usage) yield { usage };
    } finally {
      reader.releaseLock();
    }
  }
}

function safeProviderUrl(raw: string | undefined, allowedRaw: string | undefined) {
  if (!raw) return null;
  const endpoint = new URL(raw);
  const allowed = new Set((allowedRaw ?? "").split(",").map((value) => value.trim()).filter(Boolean));
  const loopback = ["localhost", "127.0.0.1", "[::1]"].includes(endpoint.hostname);
  if (endpoint.username || endpoint.password || endpoint.search || endpoint.hash) return null;
  if (!loopback && (endpoint.protocol !== "https:" || !allowed.has(endpoint.origin))) return null;
  if (loopback && !["http:", "https:"].includes(endpoint.protocol)) return null;
  return endpoint;
}

export function getGenerationProvider(): GenerationProvider {
  const kind = (process.env.SLATE_GENERATION_PROVIDER ?? "disabled").toLowerCase();
  if (kind === "fake" && process.env.NODE_ENV !== "production") return new DeterministicGenerationProvider();
  if (kind !== "openai-compatible") return new DisabledGenerationProvider();
  const key = process.env.SLATE_GENERATION_API_KEY;
  const endpoint = safeProviderUrl(process.env.SLATE_GENERATION_URL, process.env.SLATE_GENERATION_ALLOWED_ORIGINS);
  const maxInput = Number(process.env.SLATE_GENERATION_MAX_INPUT_TOKENS ?? 16_000);
  if (!key || !endpoint || !Number.isInteger(maxInput) || maxInput < 1_000 || maxInput > 200_000) return new DisabledGenerationProvider();
  return new OpenAICompatibleGenerationProvider(process.env.SLATE_GENERATION_MODEL ?? "gpt-4.1-mini", maxInput, endpoint, key);
}
