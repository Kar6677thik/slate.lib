import type { Note } from "@/lib/api/contracts";

export const KNOWLEDGE_SCHEMA_VERSION = 1;
export const KNOWLEDGE_LIMITS = {
  maxNotes: 240,
  maxClaimsPerNote: 36,
  maxCandidateNeighbors: 18,
  maxPairsPerNote: 72,
  maxIssues: 320,
  maxAiClassificationsPerJob: 8,
  classificationConcurrency: 2,
} as const;

export type ClaimType = "fact" | "decision" | "architecture" | "configuration" | "version" | "requirement" | "status" | "question" | "answer" | "preference" | "approach" | "rejection" | "supersession";
export type IssueKind = "reversal" | "value" | "version" | "status" | "architecture" | "decision" | "requirement" | "answered-question" | "supersession";
export type IssueEvidence = "documented-reversal" | "strong-conflict" | "possible-conflict" | "likely-superseded" | "possible-stale";
export type ReviewState = "open" | "resolved" | "dismissed" | "snoozed";

export interface KnowledgeClaim {
  claimId: string;
  libraryId?: string;
  noteId: string;
  revision: string;
  path: string;
  title: string;
  heading: string | null;
  text: string;
  normalizedSubject: string;
  normalizedPredicate: string;
  normalizedObject: string;
  claimType: ClaimType;
  timestamp: string | null;
  current: boolean;
  projectPath: string | null;
  component: string | null;
  tags: string[];
  contentHash: string;
  extractionMethod: "metadata" | "heading" | "pattern";
  confidence: "documented" | "strong" | "possible";
  negative: boolean;
}

export interface KnowledgeIssue {
  issueId: string;
  fingerprint: string;
  kind: IssueKind;
  evidence: IssueEvidence;
  title: string;
  explanation: string;
  state: ReviewState;
  sourceA: KnowledgeClaim;
  sourceB: KnowledgeClaim;
  staleClaimId: string | null;
  projectPath: string | null;
  currentCurrent: boolean;
  createdAt: string;
}

export interface KnowledgeReview {
  fingerprint: string;
  state: ReviewState;
  reviewedAt: string;
  reason?: string;
  snoozedUntil?: string;
}

export interface KnowledgeDiagnostics {
  claimsIndexed: number;
  openIssues: number;
  resolvedIssues: number;
  dismissedIssues: number;
  pendingAnalysis: number;
  failedAnalysis: number;
  pairsCheckedThisProcess: number;
  deterministicFindings: number;
  aiClassifiedPairs: number;
  cachedClassifications: number;
  bounded: boolean;
}

export interface KnowledgeSnapshot {
  schemaVersion: number;
  generatedAt: string;
  scope: { kind: "library" | "project" | "note"; path?: string; noteId?: string };
  issues: KnowledgeIssue[];
  diagnostics: KnowledgeDiagnostics;
  degraded?: string;
}

const headingTypes: Record<string, ClaimType> = {
  decision: "decision", decisions: "decision", architecture: "architecture",
  requirement: "requirement", requirements: "requirement", rejected: "rejection",
  superseded: "supersession", configuration: "configuration", status: "status",
};
const stop = new Set(["a", "an", "and", "are", "as", "at", "be", "by", "for", "from", "in", "is", "it", "of", "on", "or", "that", "the", "this", "to", "we", "with"]);

function hash(value: string) {
  let h = 2166136261;
  for (let i = 0; i < value.length; i++) { h ^= value.charCodeAt(i); h = Math.imul(h, 16777619); }
  return (h >>> 0).toString(36);
}
function clean(value: string) { return value.toLowerCase().replace(/[`*_\[\]{}()"']/g, " ").replace(/[^a-z0-9.+#/-]+/g, " ").replace(/\s+/g, " ").trim(); }
function words(value: string) { return clean(value).split(" ").filter((item) => item.length > 1 && !stop.has(item)); }
function projectPath(path: string) { const parts = path.replace(/\\/g, "/").split("/"); return parts.length > 1 ? parts.slice(0, Math.min(2, parts.length - 1)).join("/") : null; }
function frontmatter(markdown: string) {
  const match = markdown.match(/^---\s*\n([\s\S]*?)\n---\s*(?:\n|$)/);
  const result: Record<string, string> = {};
  if (match) for (const line of match[1].split(/\r?\n/)) {
    const field = line.match(/^([A-Za-z][\w-]*):\s*(.*?)\s*$/);
    if (field) result[field[1].toLowerCase()] = field[2].replace(/^['"]|['"]$/g, "");
  }
  return { values: result, body: match ? markdown.slice(match[0].length) : markdown };
}
function timestamp(values: Record<string, string>) {
  for (const key of ["updated", "modified", "date", "created", "timestamp"]) {
    if (values[key] && !Number.isNaN(Date.parse(values[key]))) return new Date(values[key]).toISOString();
  }
  return null;
}
function tags(values: Record<string, string>) { return (values.tags ?? "").replace(/^\[|\]$/g, "").split(/[, ]+/).map(clean).filter(Boolean).slice(0, 24); }
function componentFor(path: string, subject: string) {
  const known = ["backend", "web", "windows", "android", "database", "search", "sync", "api", "renderer"];
  return known.find((item) => clean(path + " " + subject).split(" ").includes(item)) ?? null;
}
function subjectFrom(text: string, fallback: string) {
  const left = text.split(/\b(?:is|are|uses?|requires?|must|should|port|version|status|was|were|has|have)\b/i)[0];
  const tokens = words(left).slice(-5);
  return clean(tokens.join(" ") || fallback);
}
function relation(text: string) {
  const normalized = clean(text);
  const version = text.match(/\b([A-Za-z][\w.+#-]*(?:\s+[A-Za-z][\w.+#-]*){0,2})\s+(?:version\s+)?(?:is|requires?|must use|uses?)?\s*v?(\d+(?:\.\d+){0,3})\b/i);
  const property = text.match(/\b(.{1,80}?)\s+(?:port|nodeport)\s*(?:is|=|:)?\s*(\d{2,5})\b/i);
  const status = text.match(/\b(.{1,80}?)\s+(?:is|status\s*[:=])\s*(open|answered|complete|completed|incomplete|active|inactive|deprecated|blocked|done)\b/i);
  const negative = /\b(?:do not|does not|don't|doesn't|no longer|never|without|outside|rejected|deferred)\b/i.test(text);
  if (property) return { subject: clean(property[1]), predicate: "port", object: clean(property[2]), negative, type: "configuration" as ClaimType };
  if (version) return { subject: clean(version[1]), predicate: "version", object: clean(version[2]), negative, type: "version" as ClaimType };
  if (status) return { subject: clean(status[1]), predicate: "status", object: clean(status[2]), negative, type: "status" as ClaimType };
  const use = text.match(/\b(.{1,90}?)\s+(?:no longer\s+)?(?:uses?|use|adopts?|adopt|requires?|require|must use|stores?|store|runs? on)\s+(.{1,100}?)(?:[.;]|$)/i);
  if (use) return { subject: clean(use[1]), predicate: /require|must/i.test(text) ? "requires" : /store/i.test(text) ? "storage" : "uses", object: clean(use[2]), negative, type: /require|must/i.test(text) ? "requirement" as ClaimType : "fact" as ClaimType };
  const replaced = text.match(/\b(.{1,90}?)\s+(?:was|is|has been)?\s*(?:replaced|superseded)\s+by\s+(.{1,100}?)(?:[.;]|$)/i);
  if (replaced) return { subject: clean(replaced[1]), predicate: "replaced-by", object: clean(replaced[2]), negative: false, type: "supersession" as ClaimType };
  return { subject: "", predicate: "states", object: normalized, negative, type: "fact" as ClaimType };
}
function ignored(line: string, fenced: boolean) {
  const trimmed = line.trim();
  return fenced || !trimmed || /^>/.test(trimmed) || /^ {4}/.test(line) || /^[-*]\s+(?:example|e\.g\.)\b/i.test(trimmed) || /\b(?:for example|hypothetical(?:ly)?)\b/i.test(trimmed);
}

export function extractKnowledgeClaims(note: Note, options: { current?: boolean; timestamp?: string | null } = {}) {
  const parsed = frontmatter(note.markdown);
  const result: KnowledgeClaim[] = [];
  let heading: string | null = null;
  let headingType: ClaimType | undefined;
  let fenced = false;
  const date = options.timestamp ?? timestamp(parsed.values);
  const add = (text: string, type: ClaimType, method: KnowledgeClaim["extractionMethod"], override?: Partial<ReturnType<typeof relation>>) => {
    const related = { ...relation(text), ...override };
    const subject = related.subject || subjectFrom(text, heading ?? note.title);
    if (!subject || result.length >= KNOWLEDGE_LIMITS.maxClaimsPerNote) return;
    const identity = `${note.id}|${note.revision}|${heading ?? ""}|${text}`;
    result.push({
      claimId: `claim-${hash(identity)}`, noteId: note.id, revision: note.revision, path: note.path,
      title: note.title, heading, text: text.trim().slice(0, 600), normalizedSubject: subject,
      normalizedPredicate: related.predicate, normalizedObject: related.object, claimType: type === "fact" ? related.type : type,
      timestamp: date, current: options.current !== false, projectPath: projectPath(note.path), component: componentFor(note.path, subject),
      tags: tags(parsed.values), contentHash: hash(`${clean(text)}|${related.predicate}|${related.object}`), extractionMethod: method,
      confidence: method === "metadata" || type === "supersession" ? "documented" : headingType ? "strong" : "possible", negative: related.negative,
    });
  };
  for (const key of ["status", "version", "requires", "requirement", "decision", "architecture", "superseded-by"]) {
    if (!parsed.values[key]) continue;
    const type: ClaimType = key === "superseded-by" ? "supersession" : key === "requires" || key === "requirement" ? "requirement" : key as ClaimType;
    add(`${note.title} ${key.replace("-by", "")} ${parsed.values[key]}.`, type, "metadata", key === "status" ? { subject: clean(note.title), predicate: "status", object: clean(parsed.values[key]) } : undefined);
  }
  for (const line of parsed.body.split(/\r?\n/)) {
    if (/^\s*```/.test(line)) { fenced = !fenced; continue; }
    const h = line.match(/^#{1,4}\s+(.+?)\s*#*$/);
    if (h) { heading = h[1].trim(); headingType = headingTypes[clean(heading)]; continue; }
    if (ignored(line, fenced)) continue;
    const text = line.replace(/^\s*(?:[-*+] |\d+[.)]\s+)/, "").trim();
    if (text.endsWith("?") || /^question\s*:/i.test(text)) { add(text, "question", headingType ? "heading" : "pattern", { subject: subjectFrom(text, heading ?? note.title), predicate: "asks", object: clean(text), negative: false, type: "question" }); continue; }
    const explicit = /\b(?:we (?:use|no longer use|decided|require)|requires?|must|version|port|nodeport|decision\s*:|rejected\s*:|replaced by|superseded by|is (?:open|answered|complete|completed|incomplete|deprecated|blocked)|stored? (?:in|outside)|architecture\s*:)/i.test(text);
    if (headingType || explicit) add(text, headingType ?? relation(text).type, headingType ? "heading" : "pattern");
  }
  return result;
}

function tokenOverlap(a: string, b: string) {
  const x = new Set(words(a)); const y = new Set(words(b));
  if (!x.size || !y.size) return 0;
  let shared = 0; for (const item of x) if (y.has(item)) shared++;
  return shared / Math.min(x.size, y.size);
}
function sameContext(a: KnowledgeClaim, b: KnowledgeClaim) {
  if (a.component && b.component && a.component !== b.component) return false;
  if (a.projectPath && b.projectPath && a.projectPath !== b.projectPath && tokenOverlap(a.normalizedSubject, b.normalizedSubject) < .8) return false;
  return true;
}
function newer(a: KnowledgeClaim, b: KnowledgeClaim) {
  if (a.timestamp && b.timestamp) return Date.parse(a.timestamp) > Date.parse(b.timestamp) ? a : b;
  return null;
}
function stablePair(a: KnowledgeClaim, b: KnowledgeClaim) { return [a, b].sort((x, y) => x.claimId.localeCompare(y.claimId)); }
function classify(a: KnowledgeClaim, b: KnowledgeClaim): Omit<KnowledgeIssue, "issueId" | "fingerprint" | "state" | "createdAt"> | null {
  if (a.noteId === b.noteId || !sameContext(a, b)) return null;
  const subject = tokenOverlap(a.normalizedSubject, b.normalizedSubject);
  const exactProperty = a.normalizedPredicate === b.normalizedPredicate && subject >= .65;
  const later = newer(a, b);
  const [older, newest] = later === a ? [b, a] : [a, b];
  let kind: IssueKind | null = null;
  let evidence: IssueEvidence = "possible-conflict";
  let explanation = "These current sources describe the same subject differently. Review the source context before changing either note.";
  let staleClaimId: string | null = null;
  if ((a.claimType === "supersession" || b.claimType === "supersession") && subject >= .45) {
    kind = "supersession"; evidence = "likely-superseded"; staleClaimId = later ? older.claimId : null;
    explanation = "A source explicitly describes an earlier approach as replaced or superseded.";
  } else if ((a.claimType === "question" || b.claimType === "question") && later && tokenOverlap(a.text, b.text) >= .25 && newest.claimType !== "question") {
    kind = "answered-question"; evidence = "possible-stale"; staleClaimId = older.claimId;
    explanation = "A later source may answer a question that is still recorded as open.";
  } else if (exactProperty && a.normalizedObject && b.normalizedObject && a.normalizedObject !== b.normalizedObject) {
    kind = a.normalizedPredicate === "version" ? "version" : a.normalizedPredicate === "status" ? "status" : a.normalizedPredicate === "port" ? "value" :
      a.claimType === "architecture" || b.claimType === "architecture" ? "architecture" : a.claimType === "decision" || b.claimType === "decision" ? "decision" :
      a.claimType === "requirement" || b.claimType === "requirement" ? "requirement" : "value";
    evidence = a.confidence === "documented" && b.confidence === "documented" ? "strong-conflict" : "possible-conflict";
    staleClaimId = later ? older.claimId : null;
    explanation = later ? "A newer current source records a different value for the same subject and property." : "Two current sources record different values for the same subject and property.";
  } else if (exactProperty && a.normalizedObject === b.normalizedObject && a.negative !== b.negative) {
    kind = "reversal"; evidence = "documented-reversal"; staleClaimId = later ? older.claimId : null;
    explanation = "One source affirms this statement while the other explicitly rejects it.";
  } else if (subject >= .7 && a.negative !== b.negative && tokenOverlap(a.normalizedObject, b.normalizedObject) >= .55) {
    kind = "reversal"; evidence = "strong-conflict"; staleClaimId = later ? older.claimId : null;
  }
  if (!kind) return null;
  return { kind, evidence, title: kind === "answered-question" ? "Possible answer found" : kind === "supersession" ? "Likely superseded" : `${kind[0].toUpperCase()}${kind.slice(1).replace("-", " ")} conflict`, explanation, sourceA: a, sourceB: b, staleClaimId, projectPath: a.projectPath === b.projectPath ? a.projectPath : null, currentCurrent: a.current && b.current };
}

export function analyzeKnowledgeClaims(claims: KnowledgeClaim[], reviews: Record<string, KnowledgeReview> = {}, now = new Date().toISOString()) {
  const bySubject = new Map<string, KnowledgeClaim[]>();
  for (const claim of claims) {
    const keys = [...new Set(words(claim.normalizedSubject).slice(0, 5))];
    for (const key of keys) bySubject.set(key, [...(bySubject.get(key) ?? []), claim]);
  }
  let pairsChecked = 0; let bounded = false;
  const seen = new Set<string>(); const issues: KnowledgeIssue[] = [];
  const counts = new Map<string, number>();
  for (const claim of claims) {
    const candidates = [...new Set(words(claim.normalizedSubject).flatMap((key) => bySubject.get(key) ?? []))]
      .filter((other) => other.claimId !== claim.claimId)
      .sort((a, b) => tokenOverlap(claim.normalizedSubject, b.normalizedSubject) - tokenOverlap(claim.normalizedSubject, a.normalizedSubject));
    if (candidates.length > KNOWLEDGE_LIMITS.maxCandidateNeighbors) bounded = true;
    for (const other of candidates.slice(0, KNOWLEDGE_LIMITS.maxCandidateNeighbors)) {
      if ((counts.get(claim.noteId) ?? 0) >= KNOWLEDGE_LIMITS.maxPairsPerNote) { bounded = true; break; }
      const [a, b] = stablePair(claim, other); const pair = `${a.claimId}|${b.claimId}`;
      if (seen.has(pair)) continue; seen.add(pair); pairsChecked++; counts.set(claim.noteId, (counts.get(claim.noteId) ?? 0) + 1);
      const classified = classify(a, b); if (!classified) continue;
      if (!classified.currentCurrent && classified.kind !== "supersession") continue;
      const fingerprint = `issue-${hash(`${a.claimId}:${a.contentHash}|${b.claimId}:${b.contentHash}|${classified.kind}`)}`;
      const review = reviews[fingerprint];
      issues.push({ ...classified, issueId: fingerprint, fingerprint, state: review?.state ?? "open", createdAt: now });
      if (issues.length >= KNOWLEDGE_LIMITS.maxIssues) { bounded = true; break; }
    }
    if (issues.length >= KNOWLEDGE_LIMITS.maxIssues) break;
  }
  return { issues, pairsChecked, bounded };
}

export function buildKnowledgeSnapshot(notes: Note[], scope: KnowledgeSnapshot["scope"], reviews: Record<string, KnowledgeReview> = {}, now = new Date().toISOString()): KnowledgeSnapshot {
  const boundedNotes = notes.slice(0, KNOWLEDGE_LIMITS.maxNotes);
  const claims = boundedNotes.flatMap((note) => extractKnowledgeClaims(note));
  const result = analyzeKnowledgeClaims(claims, reviews, now);
  return {
    schemaVersion: KNOWLEDGE_SCHEMA_VERSION, generatedAt: now, scope, issues: result.issues,
    diagnostics: {
      claimsIndexed: claims.length, openIssues: result.issues.filter((issue) => issue.state === "open" || issue.state === "snoozed").length,
      resolvedIssues: result.issues.filter((issue) => issue.state === "resolved").length, dismissedIssues: result.issues.filter((issue) => issue.state === "dismissed").length,
      pendingAnalysis: 0, failedAnalysis: 0, pairsCheckedThisProcess: result.pairsChecked, deterministicFindings: result.issues.length,
      aiClassifiedPairs: 0, cachedClassifications: 0, bounded: result.bounded || notes.length > boundedNotes.length,
    },
    degraded: "Deterministic analysis is active. Optional model classification is unavailable or was not needed.",
  };
}

export function applyKnowledgeReview(issue: KnowledgeIssue, state: ReviewState, reason?: string, now = new Date().toISOString()): KnowledgeReview {
  return { fingerprint: issue.fingerprint, state, reviewedAt: now, reason: reason?.trim().slice(0, 300) || undefined };
}
