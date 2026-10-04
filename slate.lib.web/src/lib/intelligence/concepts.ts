import type { Links, Note } from "@/lib/api/contracts";
import { extractKnowledgeClaims, type KnowledgeIssue } from "./knowledge-issues";
import type { KnowledgeOverlap } from "./overlap";
import type { LinkSuggestion } from "./smart-links";

export const CONCEPT_SCHEMA_VERSION = 1;
export const CONCEPT_LIMITS = {
  maxNotes: 240,
  maxConceptsPerNote: 18,
  maxAliasesPerConcept: 12,
  maxRelationshipCandidatesPerConcept: 40,
  maxRelationshipsPerConcept: 20,
  maxPageSources: 36,
  maxProjectUsages: 16,
  maxGraphNodes: 21,
  maxConcepts: 2_000,
} as const;

export type ConceptKind = "Technology" | "Framework" | "Language" | "Protocol" | "Database" | "Project" | "Organization" | "Product" | "Library" | "Service" | "Concept" | "Person" | "Other";
export type ConceptStrength = "primary" | "strong" | "supporting" | "mention";
export type ConceptRelationshipType = "related" | "used_with" | "part_of" | "depends_on" | "contrasts_with" | "replaces" | "project_co_usage" | "co_referenced";

export interface ConceptMember {
  noteId: string; revision: string; title: string; path: string; strength: ConceptStrength; score: number;
  reasons: string[]; excerpt: string; projectPath: string | null; tags: string[]; timestamp: string | null;
  decisions: string[]; questions: string[];
}
export interface ConceptRelationship {
  sourceConceptId: string; targetConceptId: string; targetName: string; relationshipType: ConceptRelationshipType;
  signals: string[]; explanation: string; sourceCount: number; projectCount: number; sourceNoteIds: string[]; schemaVersion: number;
}
export interface KnowledgeConcept {
  id: string; canonicalName: string; normalizedName: string; aliases: string[]; kind: ConceptKind;
  sourceCount: number; currentSourceCount: number; firstSeen: string | null; lastSeen: string | null;
  projectPaths: string[]; tags: string[]; importanceSignals: string[]; openQuestionCount: number; knowledgeIssueCount: number; contentHash: string; schemaVersion: number;
  members: ConceptMember[]; relationships: ConceptRelationship[];
}
export interface ConceptDiagnostics {
  conceptsIndexed: number; relationships: number; aliases: number; mergedDerivedIdentities: number; notesProcessed: number;
  pending: number; failed: number; reused: number; rebuiltThisProcess: number; boundsReached: boolean;
}
export interface ConceptSnapshot {
  schemaVersion: number; generatedAt: string; concepts: KnowledgeConcept[]; diagnostics: ConceptDiagnostics; degraded?: string;
}
export interface ConceptPageSnapshot {
  concept: KnowledgeConcept; keyNotes: ConceptMember[]; projects: Array<{ path: string; sourceCount: number; importantNote: ConceptMember; decisions: string[]; questions: string[]; recentActivity: string | null }>;
  decisions: Array<{ text: string; noteId: string; title: string; path: string }>; questions: Array<{ text: string; noteId: string; title: string; path: string }>;
  timeline: Array<{ timestamp: string; label: string; noteId: string; title: string; path: string }>;
  issues: KnowledgeIssue[]; overlaps: KnowledgeOverlap[]; linkOpportunities: LinkSuggestion[];
  graph: { nodes: Array<{ id: string; name: string; kind: ConceptKind }>; edges: ConceptRelationship[]; bounded: boolean };
  diagnostics: ConceptDiagnostics; degraded?: string;
}
export interface ConceptIdentityReview { merge?: Record<string, string>; aliases?: Record<string, string[]>; separate?: string[][]; }
export interface ConceptEvidence { links?: Map<string, Links>; issues?: KnowledgeIssue[]; overlaps?: KnowledgeOverlap[]; linkOpportunities?: LinkSuggestion[]; reviews?: ConceptIdentityReview; }

const generic = new Set(["application", "code", "data", "document", "feature", "file", "information", "issue", "note", "problem", "project", "server", "service", "system", "task", "user"]);
const known: Record<string, string> = { postgres: "PostgreSQL", postgresql: "PostgreSQL", k8s: "Kubernetes", kubernetes: "Kubernetes", dotnet: ".NET", ".net": ".NET", "opc ua": "OPC UA", "opc-ua": "OPC UA" };
const kindRules: Array<[RegExp, ConceptKind]> = [
  [/\b(postgresql|postgres|redis|mysql|sqlite|mongodb|pgvector)\b/i, "Database"],
  [/\b(kubernetes|k8s|docker|tailscale|cloudflare|kafka|mqtt|opc[ -]?ua)\b/i, "Technology"],
  [/\b(asp\.net core|next\.js|react|spring|django|fastapi)\b/i, "Framework"],
  [/\b(c\+\+|c#|typescript|javascript|python|java|rust|go)\b/i, "Language"],
  [/\b(mqtt|opc[ -]?ua|http|grpc|websocket)\b/i, "Protocol"],
  [/\b(lucene(?:\.net)?|react query|codemirror)\b/i, "Library"],
];
export function conceptHash(value: string) { let h = 2166136261; for (let i = 0; i < value.length; i++) { h ^= value.charCodeAt(i); h = Math.imul(h, 16777619); } return (h >>> 0).toString(36); }
export function normalizeConcept(value: string) {
  const normalized = value.normalize("NFKC").trim().replace(/\.md$/i, "").replace(/[–—]/g, "-").replace(/[_/]+/g, " ").replace(/\s+/g, " ");
  const key = normalized.toLowerCase().replace(/[^a-z0-9.+#-]+/g, " ").trim().replace(/\s+/g, " ");
  return key === "opc-ua" ? "opc ua" : key;
}
function canonicalFor(value: string, reviews: ConceptIdentityReview = {}) {
  const normalized = normalizeConcept(value); const reviewed = reviews.merge?.[normalized];
  if (reviewed) return reviewed;
  const named = known[normalized];
  if (named && (reviews.separate ?? []).some((pair) => pair.includes(normalized) && pair.includes(normalizeConcept(named)))) return value.trim().replace(/\s+/g, " ").replace(/(^|\s)\p{Ll}/gu, (letter) => letter.toUpperCase());
  return named ?? value.trim().replace(/\s+/g, " ").replace(/(^|\s)\p{Ll}/gu, (letter) => letter.toUpperCase());
}
function metadata(markdown: string) {
  const match = markdown.match(/^---\s*\r?\n([\s\S]*?)\r?\n---\s*(?:\r?\n|$)/); const fields: Record<string, string> = {};
  for (const line of (match?.[1] ?? "").split(/\r?\n/)) { const field = line.match(/^([A-Za-z][\w-]*):\s*(.*?)\s*$/); if (field) fields[field[1].toLowerCase()] = field[2].replace(/^['"]|['"]$/g, ""); }
  const list = (value = "") => value.replace(/^\[|\]$/g, "").split(/[,|]/).map((item) => item.trim().replace(/^['"]|['"]$/g, "")).filter(Boolean);
  return { fields, aliases: list(fields.aliases ?? fields.alias), tags: list(fields.tags ?? fields.tag), body: match ? markdown.slice(match[0].length) : markdown };
}
function projectPath(path: string) { const parts = path.replace(/\\/g, "/").split("/"); return parts.length > 1 ? parts.slice(0, Math.min(2, parts.length - 1)).join("/") : null; }
function trustedTimestamp(fields: Record<string, string>) { for (const key of ["updated", "modified", "date", "created"]) if (fields[key] && !Number.isNaN(Date.parse(fields[key]))) return new Date(fields[key]).toISOString(); return null; }
function masked(markdown: string) {
  return metadata(markdown).body
    .replace(/```[\s\S]*?```/g, " ").replace(/`[^`\n]+`/g, " ").replace(/^\s*>.*$/gm, " ")
    .replace(/https?:\/\/\S+/g, " ").replace(/\b[0-9a-f]{8}-[0-9a-f-]{27,}\b/ig, " ")
    .replace(/^\s*(?:at\s+\S+|(?:\w*(?:Exception|Error)|Error):).+$/gm, " ");
}
function acceptable(value: string) {
  const normalized = normalizeConcept(value); if (normalized.length < 2 || normalized.length > 70 || generic.has(normalized)) return false;
  if (/^(?:\d[\d:./-]*|[0-9a-f]{12,}|\w+\.(?:md|log|json|yaml|yml|txt))$/i.test(value.trim())) return false;
  return /[a-z]/i.test(value);
}
function classify(name: string, noteTypes: string[]): ConceptKind {
  for (const [pattern, kind] of kindRules) if (pattern.test(name)) return kind;
  if (noteTypes.includes("project")) return "Project";
  if (/\b(inc|corp|foundation|company|abb)\b/i.test(name)) return "Organization";
  return "Concept";
}
type Candidate = { raw: string; reason: string; score: number; excerpt: string };
function candidates(note: Note) {
  const meta = metadata(note.markdown), body = masked(note.markdown), result: Candidate[] = [], claims = extractKnowledgeClaims(note);
  const add = (raw: string, reason: string, score: number, excerpt = "") => { if (acceptable(raw)) result.push({ raw: raw.trim(), reason, score, excerpt: excerpt.trim().slice(0, 280) }); };
  add(note.title, "Dedicated note title", 100, note.title);
  meta.aliases.forEach((alias) => add(alias, "Explicit note alias", 86, note.title));
  meta.tags.forEach((tag) => add(tag.replace(/^#/, ""), "Metadata tag", 56, note.title));
  for (const match of body.matchAll(/^#{1,3}\s+(.+?)\s*#*$/gm)) add(match[1], "Named heading", 52, match[1]);
  for (const claim of claims) if (claim.normalizedSubject.split(" ").length <= 6) add(claim.normalizedSubject, `Knowledge claim · ${claim.claimType}`, claim.confidence === "possible" ? 54 : 68, claim.text);
  const technical = /(?:\b(?:PostgreSQL|Postgres|Kubernetes|k8s|Redis|MQTT|OPC[ -]UA|Kafka(?: Connect)?|Tailscale|Cloudflare|Docker|pgvector|Lucene(?:\.NET)?|ASP\.NET Core|\.NET(?: Core)?|Next\.js|TypeScript|JavaScript|Python|React Query|CodeMirror)\b)/gi;
  for (const match of body.matchAll(technical)) add(match[0], "Repeated technical reference", 45, body.slice(Math.max(0, match.index! - 70), match.index! + match[0].length + 90));
  return { meta, result, claims };
}
function relationshipType(text: string): ConceptRelationshipType {
  if (/\b(?:replace[sd]?|supersed)/i.test(text)) return "replaces";
  if (/\b(?:depend[sd]? on|requires?)\b/i.test(text)) return "depends_on";
  if (/\b(?:part of|component of)\b/i.test(text)) return "part_of";
  if (/\b(?:versus|vs\.?|contrast)/i.test(text)) return "contrasts_with";
  if (/\b(?:with|alongside|integrat)/i.test(text)) return "used_with";
  return "related";
}

export function buildConceptSnapshot(notes: Note[], evidence: ConceptEvidence = {}, now = new Date().toISOString()): ConceptSnapshot {
  const boundedNotes = notes.slice(0, CONCEPT_LIMITS.maxNotes); let bounded = notes.length > boundedNotes.length; const reviews = evidence.reviews ?? {};
  const buckets = new Map<string, { name: string; aliases: Set<string>; members: Map<string, ConceptMember>; kinds: string[] }>();
  for (const note of boundedNotes) {
    const { meta, result, claims } = candidates(note); const perIdentity = new Map<string, Candidate>();
    for (const item of result) {
      const canonical = canonicalFor(item.raw, reviews); const identity = normalizeConcept(canonical); if (!identity || (reviews.separate ?? []).some((pair) => pair.includes(normalizeConcept(item.raw)) && pair.includes(identity) && normalizeConcept(item.raw) !== identity)) continue;
      const previous = perIdentity.get(identity); if (!previous || item.score > previous.score) perIdentity.set(identity, item);
    }
    const selected = [...perIdentity].sort((a, b) => b[1].score - a[1].score).slice(0, CONCEPT_LIMITS.maxConceptsPerNote); if (perIdentity.size > selected.length) bounded = true;
    for (const [identity, item] of selected) {
      const canonicalName = canonicalFor(item.raw, reviews), bucket = buckets.get(identity) ?? { name: canonicalName, aliases: new Set<string>(), members: new Map<string, ConceptMember>(), kinds: [] };
      for (const candidate of result) if (normalizeConcept(canonicalFor(candidate.raw, reviews)) === identity) bucket.aliases.add(candidate.raw);
      (reviews.aliases?.[identity] ?? []).forEach((alias) => bucket.aliases.add(alias)); if (meta.fields.type) bucket.kinds.push(meta.fields.type.toLowerCase());
      const decisions = claims.filter((claim) => claim.claimType === "decision" && normalizeConcept(claim.text).includes(identity)).map((claim) => claim.text);
      const questions = claims.filter((claim) => claim.claimType === "question" && normalizeConcept(claim.text).includes(identity)).map((claim) => claim.text);
      const score = item.score + (decisions.length ? 16 : 0) + (questions.length ? 8 : 0); const strength: ConceptStrength = score >= 90 ? "primary" : score >= 65 ? "strong" : score >= 45 ? "supporting" : "mention";
      bucket.members.set(note.id, { noteId: note.id, revision: note.revision, title: note.title, path: note.path, strength, score, reasons: [...new Set([item.reason, ...(decisions.length ? ["Contains a decision"] : []), ...(questions.length ? ["Contains an open question"] : [])])], excerpt: item.excerpt || note.title, projectPath: projectPath(note.path), tags: meta.tags, timestamp: trustedTimestamp(meta.fields), decisions, questions });
      buckets.set(identity, bucket);
    }
  }
  const concepts: KnowledgeConcept[] = [...buckets].filter(([, bucket]) => bucket.members.size >= 2 || [...bucket.members.values()].some((member) => member.score >= 45)).slice(0, CONCEPT_LIMITS.maxConcepts).map(([identity, bucket]): KnowledgeConcept => {
    const members = [...bucket.members.values()].sort((a, b) => b.score - a.score || a.path.localeCompare(b.path)); const projects = [...new Set(members.flatMap((member) => member.projectPath ? [member.projectPath] : []))]; const dates = members.flatMap((member) => member.timestamp ? [member.timestamp] : []).sort();
    const aliases = [...bucket.aliases].filter((alias) => normalizeConcept(alias) !== identity).slice(0, CONCEPT_LIMITS.maxAliasesPerConcept); if (bucket.aliases.size > aliases.length + 1) bounded = true;
    const memberIds = new Set(members.map((member) => member.noteId));
    const openQuestionCount = members.reduce((sum, member) => sum + member.questions.length, 0);
    const knowledgeIssueCount = (evidence.issues ?? []).filter((issue) => memberIds.has(issue.sourceA.noteId) || memberIds.has(issue.sourceB.noteId)).length;
    const importanceSignals = [...(members.some((member) => member.reasons.includes("Dedicated note title")) ? ["Dedicated note"] : []), ...(projects.length > 1 ? [`Appears across ${projects.length} projects`] : []), ...(members.reduce((sum, member) => sum + member.decisions.length, 0) ? [`Contains ${members.reduce((sum, member) => sum + member.decisions.length, 0)} decisions`] : []), ...(openQuestionCount ? [`Has ${openQuestionCount} open question${openQuestionCount === 1 ? "" : "s"}`] : []), ...(knowledgeIssueCount ? [`Has ${knowledgeIssueCount} knowledge issue${knowledgeIssueCount === 1 ? "" : "s"}`] : []), ...(members.length > 1 ? [`Referenced in ${members.length} notes`] : [])];
    return { id: `concept-${conceptHash(identity)}`, canonicalName: bucket.name, normalizedName: identity, aliases, kind: classify(bucket.name, bucket.kinds), sourceCount: members.length, currentSourceCount: members.length, firstSeen: dates[0] ?? null, lastSeen: dates.at(-1) ?? null, projectPaths: projects, tags: [...new Set(members.flatMap((member) => member.tags))].slice(0, 20), importanceSignals, openQuestionCount, knowledgeIssueCount, contentHash: conceptHash(members.map((member) => `${member.noteId}:${member.revision}`).join("|")), schemaVersion: CONCEPT_SCHEMA_VERSION, members, relationships: [] };
  });
  const byNote = new Map<string, KnowledgeConcept[]>(); for (const concept of concepts) for (const member of concept.members) byNote.set(member.noteId, [...(byNote.get(member.noteId) ?? []), concept]);
  const relationshipEvidence = new Map<string, { notes: Set<string>; projects: Set<string>; signals: Set<string>; text: string }>();
  for (const [noteId, noteConcepts] of byNote) {
    const limited = noteConcepts.sort((a, b) => (b.members.find((m) => m.noteId === noteId)?.score ?? 0) - (a.members.find((m) => m.noteId === noteId)?.score ?? 0)).slice(0, 8);
    for (let a = 0; a < limited.length; a++) for (let b = a + 1; b < limited.length; b++) {
      const left = limited[a], right = limited[b], key = [left.id, right.id].sort().join("|"); const state = relationshipEvidence.get(key) ?? { notes: new Set(), projects: new Set(), signals: new Set(), text: "" };
      state.notes.add(noteId); const member = left.members.find((item) => item.noteId === noteId); if (member?.projectPath) state.projects.add(member.projectPath); state.signals.add("Named together in a source note"); state.text += ` ${member?.excerpt ?? ""}`; relationshipEvidence.set(key, state);
    }
  }
  for (const concept of concepts) {
    const candidates: ConceptRelationship[] = [];
    for (const [key, state] of relationshipEvidence) {
      const ids = key.split("|"); if (!ids.includes(concept.id)) continue; const targetId = ids.find((id) => id !== concept.id)!; const target = concepts.find((item) => item.id === targetId); if (!target) continue;
      const explicit = [...state.notes].some((noteId) => evidence.links?.get(noteId)?.outgoing.some((link) => target.members.some((member) => member.noteId === link.targetId)));
      const signals = [...state.signals, ...(explicit ? ["Connected by an authored note link"] : []), ...(state.projects.size > 1 ? [`Used across ${state.projects.size} shared projects`] : [])];
      if (state.notes.size < 2 && !explicit) continue;
      const type = explicit ? relationshipType(state.text) : state.projects.size ? "project_co_usage" : "co_referenced";
      candidates.push({ sourceConceptId: concept.id, targetConceptId: target.id, targetName: target.canonicalName, relationshipType: type, signals, explanation: explicit ? `${concept.canonicalName} and ${target.canonicalName} are connected by authored links and shared source evidence.` : `Appears with ${target.canonicalName} in ${state.notes.size} notes${state.projects.size ? ` across ${state.projects.size} project${state.projects.size === 1 ? "" : "s"}` : ""}.`, sourceCount: state.notes.size, projectCount: state.projects.size, sourceNoteIds: [...state.notes].slice(0, 12), schemaVersion: CONCEPT_SCHEMA_VERSION });
    }
    if (candidates.length > CONCEPT_LIMITS.maxRelationshipCandidatesPerConcept) bounded = true;
    concept.relationships = candidates.sort((a, b) => Number(b.signals.includes("Connected by an authored note link")) - Number(a.signals.includes("Connected by an authored note link")) || b.sourceCount - a.sourceCount).slice(0, CONCEPT_LIMITS.maxRelationshipsPerConcept);
  }
  concepts.sort((a, b) => b.projectPaths.length - a.projectPaths.length || b.sourceCount - a.sourceCount || a.canonicalName.localeCompare(b.canonicalName));
  return { schemaVersion: CONCEPT_SCHEMA_VERSION, generatedAt: now, concepts, diagnostics: { conceptsIndexed: concepts.length, relationships: concepts.reduce((sum, concept) => sum + concept.relationships.length, 0), aliases: concepts.reduce((sum, concept) => sum + concept.aliases.length, 0), mergedDerivedIdentities: Object.keys(reviews.merge ?? {}).length, notesProcessed: boundedNotes.length, pending: 0, failed: 0, reused: 0, rebuiltThisProcess: concepts.length, boundsReached: bounded }, degraded: "Deterministic concept pages are active; generation is optional." };
}

export function buildConceptPage(snapshot: ConceptSnapshot, identity: string, evidence: ConceptEvidence = {}): ConceptPageSnapshot | null {
  const normalized = normalizeConcept(identity); const concept = snapshot.concepts.find((item) => item.id === identity || item.normalizedName === normalized || item.aliases.some((alias) => normalizeConcept(alias) === normalized)); if (!concept) return null;
  const memberIds = new Set(concept.members.map((member) => member.noteId)); const projects = concept.projectPaths.slice(0, CONCEPT_LIMITS.maxProjectUsages).map((path) => { const members = concept.members.filter((member) => member.projectPath === path); return { path, sourceCount: members.length, importantNote: members[0], decisions: members.flatMap((member) => member.decisions).slice(0, 5), questions: members.flatMap((member) => member.questions).slice(0, 5), recentActivity: members.flatMap((member) => member.timestamp ? [member.timestamp] : []).sort().at(-1) ?? null }; });
  const decisions = concept.members.flatMap((member) => member.decisions.map((text) => ({ text, noteId: member.noteId, title: member.title, path: member.path }))).slice(0, 20);
  const questions = concept.members.flatMap((member) => member.questions.map((text) => ({ text, noteId: member.noteId, title: member.title, path: member.path }))).slice(0, 20);
  const timeline = concept.members.flatMap((member) => member.timestamp ? [{ timestamp: member.timestamp, label: member.reasons[0], noteId: member.noteId, title: member.title, path: member.path }] : []).sort((a, b) => b.timestamp.localeCompare(a.timestamp)).slice(0, 30);
  const graphRelationships = concept.relationships.slice(0, CONCEPT_LIMITS.maxGraphNodes - 1), related = new Map(snapshot.concepts.map((item) => [item.id, item]));
  return { concept, keyNotes: concept.members.slice(0, CONCEPT_LIMITS.maxPageSources), projects, decisions, questions, timeline, issues: (evidence.issues ?? []).filter((item) => memberIds.has(item.sourceA.noteId) || memberIds.has(item.sourceB.noteId)), overlaps: (evidence.overlaps ?? []).filter((item) => memberIds.has(item.noteA.noteId) || memberIds.has(item.noteB.noteId)), linkOpportunities: (evidence.linkOpportunities ?? []).filter((item) => memberIds.has(item.sourceNoteId) || memberIds.has(item.target.noteId)), graph: { nodes: [{ id: concept.id, name: concept.canonicalName, kind: concept.kind }, ...graphRelationships.flatMap((relationship) => { const target = related.get(relationship.targetConceptId); return target ? [{ id: target.id, name: target.canonicalName, kind: target.kind }] : []; })], edges: graphRelationships, bounded: concept.relationships.length >= CONCEPT_LIMITS.maxGraphNodes }, diagnostics: snapshot.diagnostics, degraded: snapshot.degraded };
}

export function conceptsForNote(snapshot: ConceptSnapshot, noteId: string, limit = 8) { return snapshot.concepts.filter((concept) => concept.members.some((member) => member.noteId === noteId)).sort((a, b) => (b.members.find((member) => member.noteId === noteId)?.score ?? 0) - (a.members.find((member) => member.noteId === noteId)?.score ?? 0)).slice(0, Math.min(limit, 12)); }
