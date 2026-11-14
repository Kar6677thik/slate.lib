/*
 * Local CPU reproduction for whole-library analyses. Shared by analysis.perf.ts (Vitest) and profile.ts (plain Node with --cpu-prof). It builds a synthetic library and reports how long each synchronous analysis blocks the event loop.
 */
import type { Links, Note } from "@/lib/api/contracts";
import { analyzeOverlaps } from "@/lib/intelligence/overlap";
import { buildKnowledgeSnapshot, extractKnowledgeClaims } from "@/lib/intelligence/knowledge-issues";
import { buildConceptSnapshot } from "@/lib/intelligence/concepts";
import { analyzeSmartLinks } from "@/lib/intelligence/smart-links";
import { analyzeKnowledgeGaps } from "@/lib/intelligence/knowledge-gaps";
import { chunkMarkdown } from "@/lib/intelligence/chunking";

const words = "kubernetes postgres redis cache index queue lease retry backoff tailscale ingress service pod deployment secret config volume backup restore snapshot replica leader follower raft paxos consensus quorum shard partition tenant token oauth scope audience issuer jwks signature hash bloom filter btree heap vacuum mvcc wal checkpoint latency throughput bandwidth socket thread event loop worker promise stream buffer parser lexer grammar compiler runtime garbage collector allocator memory page cache tlb kernel syscall scheduler mutex semaphore channel actor message broker kafka rabbit nats grpc http rest graphql schema migration rollout canary probe liveness readiness metric trace log span alert dashboard budget slo incident postmortem runbook terraform helm kustomize docker image registry digest attestation".split(" ");
const technologies = ["PostgreSQL", "Redis", "Kubernetes", "Tailscale", "Lucene", "pgvector", "Next.js", "React", "ASP.NET Core", "Kafka", "gRPC", "Terraform", "Docker", "Git", "OAuth", "WireGuard", "SQLite", "Rust", "Go", "TypeScript"];

let seed = 42;
const random = () => ((seed = (seed * 1103515245 + 12345) % 2147483648) / 2147483648);
const pick = <T,>(values: T[]) => values[Math.floor(random() * values.length)];
const sentence = (n: number) => Array.from({ length: n }, () => pick(words)).join(" ");

function syntheticNote(index: number, titles: string[], size: number): Note {
  const title = titles[index];
  const folder = `Projects/${pick(["slate", "orbit", "sprawl", "megatron", "study"])}/${pick(["design", "ops", "notes", "research"])}`;
  const parts = [`---\nid: ${index}\ntags: [${pick(words)}, ${pick(words)}]\naliases: [${title} notes]\n---`, `# ${title}`, ""];
  while (parts.join("\n").length < size) {
    const roll = random();
    if (roll < 0.15) parts.push(`## ${pick(technologies)} ${pick(words)}`, "");
    else if (roll < 0.35) parts.push(`We use ${pick(technologies)} for ${sentence(4)}. ${pick(technologies)} is ${pick(["faster", "slower", "required", "deprecated"])} than ${pick(technologies)}.`, "");
    else if (roll < 0.5) parts.push(`- ${sentence(6)} [[${pick(titles)}]]`, `- ${sentence(8)}`, "");
    else if (roll < 0.58) parts.push("```ts", `const ${pick(words)} = await ${pick(words)}(${pick(words)});`.repeat(3), "```", "");
    else if (roll < 0.62) parts.push(`Why does ${pick(technologies)} need ${pick(words)}?`, "");
    else parts.push(`${sentence(14)}. ${sentence(10)} ${pick(technologies)} ${sentence(9)}.`, "");
  }
  return { id: `00000000-0000-4000-8000-${String(index + 1).padStart(12, "0")}`, title, path: `${folder}/${title.toLowerCase().replace(/\W+/g, "-")}.md`, markdown: parts.join("\n"), revision: `r${index}` } as Note;
}

export function library(count: number, { large = 0, largeSize = 400_000 } = {}) {
  const titles = Array.from({ length: count }, (_, i) => `${pick(technologies)} ${pick(words)} ${i}`);
  return titles.map((_, i) => syntheticNote(i, titles, i < large ? largeSize : 1_500 + Math.floor(random() * 6_000)));
}

const NOW = "2026-11-14T00:00:00.000Z";
function fingerprint(value: unknown) {
  const text = JSON.stringify(value, (key, item) => (key === "generatedAt" || key === "createdAt" ? undefined : item));
  let h = 2166136261; for (let i = 0; i < text.length; i++) { h ^= text.charCodeAt(i); h = Math.imul(h, 16777619); }
  return `${(h >>> 0).toString(16).padStart(8, "0")}/${text.length}`;
}
function time<T>(label: string, operation: () => T) {
  const started = performance.now();
  const value = operation();
  const ms = performance.now() - started;
  console.log(`${label.padEnd(44)} ${ms.toFixed(0).padStart(7)} ms   ${fingerprint(value)}`);
  return value;
}

export function run(name: string, notes: Note[]) {
  console.log(`\n== ${name}: ${notes.length} notes, ${(notes.reduce((n, note) => n + note.markdown.length, 0) / 1024 / 1024).toFixed(1)} MiB`);
  const links = new Map<string, Links>(notes.map((note) => [note.id, { noteId: note.id, outgoing: [], backlinks: [] }]));
  const scope = { kind: "library" } as const;
  time("chunkMarkdown (all notes)", () => notes.map((note) => chunkMarkdown(note)));
  time("extractKnowledgeClaims (all notes)", () => notes.map((note) => extractKnowledgeClaims(note)));
  const knowledge = time("buildKnowledgeSnapshot", () => buildKnowledgeSnapshot(notes, scope, {}, NOW));
  const overlaps = time("analyzeOverlaps", () => analyzeOverlaps(notes, scope, NOW).findings);
  const concepts = time("buildConceptSnapshot", () => buildConceptSnapshot(notes, { links, issues: knowledge.issues }, NOW).concepts);
  const suggestions = time("analyzeSmartLinks", () => analyzeSmartLinks(notes, scope, { links, issues: knowledge.issues, overlaps }, NOW).suggestions);
  time("analyzeKnowledgeGaps", () => analyzeKnowledgeGaps({ libraryId: "lib", notes, concepts, issues: knowledge.issues, overlaps, linkSuggestions: suggestions }).gaps);
}

