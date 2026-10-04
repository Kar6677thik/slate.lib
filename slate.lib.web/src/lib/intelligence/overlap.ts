import type { Note } from "@/lib/api/contracts";
import { extractKnowledgeClaims } from "./knowledge-issues";

export const OVERLAP_SCHEMA_VERSION = 1;
export const OVERLAP_LIMITS = {
  maxNotes: 240,
  maxSectionsPerNote: 32,
  maxCandidateNeighbors: 20,
  maxPairsPerNote: 72,
  maxFindings: 320,
  maxModelClassifications: 8,
  classificationConcurrency: 2,
} as const;

export type OverlapKind = "exact-duplicate" | "near-duplicate" | "partial-overlap" | "possibly-absorbed" | "fragmented" | "duplicate-capture";
export type OverlapReviewState = "open" | "keep-separate" | "resolved" | "dismissed";
export interface OverlapSection { heading: string; text: string; normalized: string; hash: string; tokens: string[]; }
export interface OverlapNote {
  noteId: string; revision: string; path: string; title: string; contentHash: string; noteType: string | null;
  timestamp: string | null; projectPath: string | null; headings: string[]; sections: OverlapSection[]; claims: string[];
  normalizedContent: string; wordCount: number;
}
export interface KnowledgeOverlap {
  id: string; fingerprint: string; kind: OverlapKind; state: OverlapReviewState; label: string; explanation: string;
  noteA: OverlapNote; noteB: OverlapNote; sharedSections: OverlapSection[]; uniqueSectionsA: OverlapSection[];
  uniqueSectionsB: OverlapSection[]; sharedClaims: string[]; deterministicSignals: string[]; classification: "deterministic" | "model";
  projectPath: string | null; hasKnowledgeIssue: boolean; createdAt: string; schemaVersion: number;
}
export interface OverlapDiagnostics {
  exactDuplicates: number; nearDuplicates: number; partialOverlaps: number; possiblyAbsorbed: number;
  consolidationOpportunities: number; openReviewItems: number; reviewedItems: number; pairsAnalyzedThisProcess: number;
  deterministicClassifications: number; modelClassifications: number; cacheHits: number; pendingJobs: number;
  failedJobs: number; bounded: boolean;
}
export interface OverlapSnapshot {
  schemaVersion: number; generatedAt: string; scope: { kind: "library" | "project" | "note"; path?: string; noteId?: string };
  findings: KnowledgeOverlap[]; diagnostics: OverlapDiagnostics; degraded?: string;
}

const ignoredMetadata = new Set(["id", "uuid", "note-id", "revision", "created", "updated", "modified", "date", "timestamp", "exported", "capture-id", "generated-at"]);
const weakHeadings = new Set(["overview", "introduction", "notes", "summary", "references", "links", "resources"]);
const stop = new Set(["a", "an", "and", "are", "as", "at", "be", "by", "for", "from", "in", "is", "it", "of", "on", "or", "that", "the", "this", "to", "we", "with"]);

function hash(value: string) { let h = 2166136261; for (let index = 0; index < value.length; index++) { h ^= value.charCodeAt(index); h = Math.imul(h, 16777619); } return (h >>> 0).toString(36); }
function cleanWords(value: string) { return value.toLowerCase().replace(/https?:\/\/\S+/g, " link ").replace(/[^a-z0-9.+#/-]+/g, " ").split(/\s+/).filter((item) => item.length > 1 && !stop.has(item)); }
function unique<T>(items: T[]) { return [...new Set(items)]; }
function projectPath(path: string) { const parts = path.replace(/\\/g, "/").split("/"); return parts.length > 1 ? parts.slice(0, Math.min(2, parts.length - 1)).join("/") : null; }

function parseFrontmatter(markdown: string) {
  const match = markdown.replace(/\r\n?/g, "\n").match(/^---\s*\n([\s\S]*?)\n---\s*(?:\n|$)/);
  const metadata: [string, string][] = [];
  if (match) for (const line of match[1].split("\n")) {
    const field = line.match(/^([A-Za-z][\w-]*):\s*(.*?)\s*$/);
    if (field && !ignoredMetadata.has(field[1].toLowerCase())) metadata.push([field[1].toLowerCase(), field[2].trim().replace(/^['"]|['"]$/g, "")]);
  }
  const raw = Object.fromEntries((match?.[1] ?? "").split("\n").map((line) => line.match(/^([A-Za-z][\w-]*):\s*(.*?)\s*$/)).filter(Boolean).map((field) => [field![1].toLowerCase(), field![2].trim().replace(/^['"]|['"]$/g, "")]));
  return { metadata: metadata.sort(([a], [b]) => a.localeCompare(b)), raw, body: match ? markdown.replace(/\r\n?/g, "\n").slice(match[0].length) : markdown.replace(/\r\n?/g, "\n") };
}

export function normalizeOverlapContent(markdown: string) {
  const parsed = parseFrontmatter(markdown);
  let fenced = false;
  const lines = parsed.body.split("\n").map((line) => {
    if (/^\s*```/.test(line)) { fenced = !fenced; return line.trim(); }
    if (fenced) return line.replace(/[ \t]+$/g, "");
    return line.trim()
      .replace(/^#{1,6}\s*/, "# ")
      .replace(/^[-+*]\s+/, "- ")
      .replace(/^(\d+)[.)]\s+/, "$1. ")
      .replace(/\*\*([^*]+)\*\*|__([^_]+)__/g, "$1$2")
      .replace(/(?<!\*)\*([^*]+)\*(?!\*)|(?<!_)_([^_]+)_(?!_)/g, "$1$2")
      .replace(/[ \t]+/g, " ");
  });
  const body = lines.join("\n").replace(/\n{3,}/g, "\n\n").trim();
  const metadata = parsed.metadata.map(([key, value]) => `${key}:${value.replace(/\s+/g, " ").toLowerCase()}`).join("\n");
  return [metadata, body].filter(Boolean).join("\n---\n");
}

function sectionize(markdown: string) {
  const { body } = parseFrontmatter(markdown);
  const sections: { heading: string; lines: string[] }[] = [{ heading: "Opening", lines: [] }];
  let fenced = false;
  for (const line of body.split("\n")) {
    if (/^\s*```/.test(line)) fenced = !fenced;
    const heading = !fenced ? line.match(/^#{1,4}\s+(.+?)\s*#*$/) : null;
    if (heading) sections.push({ heading: heading[1].trim(), lines: [] }); else sections.at(-1)!.lines.push(line);
  }
  return sections.filter((section) => section.lines.join(" ").trim()).slice(0, OVERLAP_LIMITS.maxSectionsPerNote).map((section) => {
    const text = section.lines.join("\n").trim().slice(0, 2_000);
    const normalized = normalizeOverlapContent(`# ${section.heading}\n${text}`);
    return { heading: section.heading, text, normalized, hash: hash(normalized), tokens: unique(cleanWords(text)).slice(0, 240) };
  });
}

function noteType(markdown: string) { const { raw } = parseFrontmatter(markdown); return String(raw.type ?? "").toLowerCase() || null; }
function timestamp(markdown: string) { const { raw } = parseFrontmatter(markdown); for (const key of ["updated", "modified", "date", "created", "timestamp"]) if (raw[key] && !Number.isNaN(Date.parse(raw[key]))) return new Date(raw[key]).toISOString(); return null; }

export function buildOverlapNote(note: Note): OverlapNote {
  const normalizedContent = normalizeOverlapContent(note.markdown);
  const sections = sectionize(note.markdown);
  const claims = extractKnowledgeClaims(note).map((claim) => `${claim.normalizedSubject}|${claim.normalizedPredicate}|${claim.normalizedObject}`);
  return { noteId: note.id, revision: note.revision, path: note.path, title: note.title, contentHash: hash(normalizedContent), noteType: noteType(note.markdown), timestamp: timestamp(note.markdown), projectPath: projectPath(note.path), headings: sections.map((section) => section.heading), sections, claims, normalizedContent, wordCount: cleanWords(normalizedContent).length };
}

function jaccard(left: string[], right: string[]) { const a = new Set(left), b = new Set(right); if (!a.size || !b.size) return 0; let shared = 0; for (const item of a) if (b.has(item)) shared++; return shared / (a.size + b.size - shared); }
function containment(left: string[], right: string[]) { const a = new Set(left), b = new Set(right); if (!a.size || !b.size) return 0; let shared = 0; for (const item of a) if (b.has(item)) shared++; return shared / Math.min(a.size, b.size); }
function intentSuppressed(a: OverlapNote, b: OverlapNote) {
  const types = new Set([a.noteType, b.noteType]);
  if (["template", "daily"].some((type) => types.has(type))) return true;
  if (["index", "moc"].some((type) => types.has(type)) && a.contentHash !== b.contentHash) return true;
  if ((types.has("question") || types.has("answer")) && a.contentHash !== b.contentHash) return true;
  return false;
}
function sectionMatch(a: OverlapSection, b: OverlapSection) {
  if (a.hash === b.hash) return 1;
  const content = jaccard(a.tokens, b.tokens);
  const heading = jaccard(cleanWords(a.heading), cleanWords(b.heading));
  return content * .82 + heading * .18;
}
function newer(a: OverlapNote, b: OverlapNote) { if (!a.timestamp || !b.timestamp || a.timestamp === b.timestamp) return null; return Date.parse(a.timestamp) > Date.parse(b.timestamp) ? a : b; }
function label(kind: OverlapKind) { return ({ "exact-duplicate": "Exact duplicate", "near-duplicate": "Near duplicate", "partial-overlap": "Partial overlap", "possibly-absorbed": "Possibly absorbed", fragmented: "Possible consolidation opportunity", "duplicate-capture": "Duplicate capture" })[kind]; }

function compare(a: OverlapNote, b: OverlapNote, now: string): KnowledgeOverlap | null {
  if (intentSuppressed(a, b)) return null;
  const tokensA = cleanWords(a.normalizedContent), tokensB = cleanWords(b.normalizedContent);
  const lexical = jaccard(tokensA, tokensB), contained = containment(tokensA, tokensB);
  const headingSimilarity = jaccard(a.headings.flatMap(cleanWords).filter((item) => !weakHeadings.has(item)), b.headings.flatMap(cleanWords).filter((item) => !weakHeadings.has(item)));
  const sharedClaims = a.claims.filter((claim) => b.claims.includes(claim));
  const matches: { a: OverlapSection; b: OverlapSection; score: number }[] = [];
  const usedB = new Set<string>();
  for (const sectionA of a.sections) {
    const best = b.sections.filter((section) => !usedB.has(section.hash)).map((sectionB) => ({ a: sectionA, b: sectionB, score: sectionMatch(sectionA, sectionB) })).sort((x, y) => y.score - x.score)[0];
    if (best && best.score >= .56) { matches.push(best); usedB.add(best.b.hash); }
  }
  const sharedSections = matches.map((match) => ({ ...match.a, heading: match.a.heading === "Opening" ? match.b.heading : match.a.heading }));
  const uniqueSectionsA = a.sections.filter((section) => !matches.some((match) => match.a.hash === section.hash));
  const uniqueSectionsB = b.sections.filter((section) => !matches.some((match) => match.b.hash === section.hash));
  const coverageA = a.sections.length ? matches.length / a.sections.length : 0;
  const coverageB = b.sections.length ? matches.length / b.sections.length : 0;
  const chronological = newer(a, b);
  const capture = /(^|\/)inbox(\/|$)/i.test(a.path) || /(^|\/)inbox(\/|$)/i.test(b.path);
  let kind: OverlapKind | null = null;
  const signals: string[] = [];
  if (a.contentHash === b.contentHash) { kind = capture ? "duplicate-capture" : "exact-duplicate"; signals.push("Identical normalized meaningful content"); }
  else if (Math.min(coverageA, coverageB) >= .72 && lexical >= .62 && (headingSimilarity >= .45 || sharedClaims.length || contained >= .78)) { kind = capture ? "duplicate-capture" : "near-duplicate"; signals.push("Most sections and knowledge are shared"); }
  else if (chronological && Math.max(coverageA, coverageB) >= .75 && contained >= .72 && (uniqueSectionsA.length || uniqueSectionsB.length)) {
    const older = chronological.noteId === a.noteId ? b : a;
    const olderCoverage = older.noteId === a.noteId ? coverageA : coverageB;
    if (olderCoverage >= .75) { kind = "possibly-absorbed"; signals.push("Most older-note sections occur in the newer note", "Newer note retains additional material"); }
  }
  if (!kind && matches.length && (matches.some((match) => match.score >= .72) || sharedClaims.length) && Math.min(coverageA, coverageB) < .8) { kind = "partial-overlap"; signals.push(`${matches.length} substantial section match${matches.length === 1 ? "" : "es"}`); }
  if (!kind && a.wordCount <= 260 && b.wordCount <= 260 && lexical >= .08 && lexical < .42 && headingSimilarity >= .25 && a.projectPath === b.projectPath && a.projectPath) { kind = "fragmented"; signals.push("Small notes share a focused subject but little duplicated text"); }
  if (!kind) return null;
  const ordered = [a, b].sort((x, y) => x.noteId.localeCompare(y.noteId));
  const fingerprint = `overlap-${hash(`${ordered[0].noteId}:${ordered[0].contentHash}|${ordered[1].noteId}:${ordered[1].contentHash}|${kind}|v${OVERLAP_SCHEMA_VERSION}`)}`;
  const explanation = kind === "exact-duplicate" || kind === "duplicate-capture" ? "The meaningful normalized content is identical; paths, titles, identity metadata, or timestamps may differ."
    : kind === "near-duplicate" ? "These notes share most sections, claims, and meaningful wording while retaining only limited unique material."
    : kind === "partial-overlap" ? "One or more substantial sections cover the same knowledge, while each note still serves a broader distinct purpose."
    : kind === "possibly-absorbed" ? "Most of the older note appears in a newer current note, but unique material remains and should be reviewed."
    : "These small notes cover complementary parts of a common subject and may benefit from an overview or joint review.";
  return { id: fingerprint, fingerprint, kind, state: "open", label: label(kind), explanation, noteA: a, noteB: b, sharedSections, uniqueSectionsA, uniqueSectionsB, sharedClaims, deterministicSignals: signals, classification: "deterministic", projectPath: a.projectPath === b.projectPath ? a.projectPath : null, hasKnowledgeIssue: false, createdAt: now, schemaVersion: OVERLAP_SCHEMA_VERSION };
}

export function analyzeOverlaps(notes: Note[], scope: OverlapSnapshot["scope"], now = new Date().toISOString()): OverlapSnapshot {
  const boundedNotes = notes.slice(0, OVERLAP_LIMITS.maxNotes).map(buildOverlapNote);
  const exact = new Map<string, OverlapNote[]>(), byToken = new Map<string, OverlapNote[]>();
  for (const note of boundedNotes) {
    exact.set(note.contentHash, [...(exact.get(note.contentHash) ?? []), note]);
    const keys = unique([...cleanWords(note.title), ...note.headings.flatMap(cleanWords)].filter((key) => !weakHeadings.has(key))).slice(0, 12);
    for (const key of keys) byToken.set(key, [...(byToken.get(key) ?? []), note]);
  }
  const findings: KnowledgeOverlap[] = [], seen = new Set<string>(), perNote = new Map<string, number>();
  let pairs = 0, bounded = notes.length > boundedNotes.length;
  for (const note of boundedNotes) {
    const keys = unique([...cleanWords(note.title), ...note.headings.flatMap(cleanWords)].filter((key) => !weakHeadings.has(key))).slice(0, 12);
    const candidates = unique([...(exact.get(note.contentHash) ?? []), ...keys.flatMap((key) => byToken.get(key) ?? [])])
      .filter((other) => other.noteId !== note.noteId)
      .sort((a, b) => containment(cleanWords(note.title + " " + note.headings.join(" ")), cleanWords(b.title + " " + b.headings.join(" "))) - containment(cleanWords(note.title + " " + note.headings.join(" ")), cleanWords(a.title + " " + a.headings.join(" "))));
    if (candidates.length > OVERLAP_LIMITS.maxCandidateNeighbors) bounded = true;
    for (const other of candidates.slice(0, OVERLAP_LIMITS.maxCandidateNeighbors)) {
      if ((perNote.get(note.noteId) ?? 0) >= OVERLAP_LIMITS.maxPairsPerNote) { bounded = true; break; }
      const pair = [note.noteId, other.noteId].sort().join("|"); if (seen.has(pair)) continue;
      seen.add(pair); pairs++; perNote.set(note.noteId, (perNote.get(note.noteId) ?? 0) + 1);
      const finding = compare(note, other, now); if (finding) findings.push(finding);
      if (findings.length >= OVERLAP_LIMITS.maxFindings) { bounded = true; break; }
    }
    if (findings.length >= OVERLAP_LIMITS.maxFindings) break;
  }
  const scoped = scope.kind === "note" ? findings.filter((item) => item.noteA.noteId === scope.noteId || item.noteB.noteId === scope.noteId) : findings;
  return { schemaVersion: OVERLAP_SCHEMA_VERSION, generatedAt: now, scope, findings: scoped, diagnostics: {
    exactDuplicates: scoped.filter((item) => item.kind === "exact-duplicate" || item.kind === "duplicate-capture").length,
    nearDuplicates: scoped.filter((item) => item.kind === "near-duplicate").length,
    partialOverlaps: scoped.filter((item) => item.kind === "partial-overlap").length,
    possiblyAbsorbed: scoped.filter((item) => item.kind === "possibly-absorbed").length,
    consolidationOpportunities: scoped.filter((item) => item.kind === "fragmented").length,
    openReviewItems: scoped.length, reviewedItems: 0, pairsAnalyzedThisProcess: pairs, deterministicClassifications: scoped.length,
    modelClassifications: 0, cacheHits: 0, pendingJobs: 0, failedJobs: 0, bounded,
  }, degraded: "Deterministic overlap analysis is active. Optional model classification is unavailable or was not needed." };
}
