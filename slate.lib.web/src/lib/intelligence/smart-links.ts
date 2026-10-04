import type { Links, Note } from "@/lib/api/contracts";
import { extractKnowledgeClaims, type KnowledgeIssue } from "./knowledge-issues";
import { analyzeOverlaps, type KnowledgeOverlap } from "./overlap";

export const SMART_LINK_SCHEMA_VERSION = 1;
export const SMART_LINK_LIMITS = {
  maxNotes: 180,
  maxMentionsPerNote: 24,
  maxTargetCandidatesPerMention: 6,
  maxCandidateNeighbors: 18,
  maxGraphNeighbors: 24,
  maxSemanticSources: 24,
  maxSemanticNeighbors: 8,
  maxSuggestionsPerNote: 15,
  maxSuggestions: 360,
} as const;

export type LinkSuggestionType = "mention" | "related" | "graph_bridge" | "project_relationship" | "concept_reference" | "better_target" | "overview_relationship";
export type LinkSuggestionStatus = "open" | "inserted" | "dismissed" | "not-relevant";
export interface LinkTarget { noteId: string; revision: string; contentHash: string; title: string; path: string; heading?: string | null; noteType?: string | null; }
export interface LinkSuggestion {
  id: string; fingerprint: string; sourceNoteId: string; sourceRevision: string; sourceHash: string; sourceTitle: string; sourcePath: string;
  target: LinkTarget; alternateTargets: LinkTarget[]; sourceHeading?: string | null; sourceExcerpt: string; sourceRange?: { from: number; to: number };
  existingText?: string; suggestedMarkdown?: string; suggestionType: LinkSuggestionType; suggestedLinkKind: "wiki" | "wiki-heading";
  signals: string[]; explanation: string; status: LinkSuggestionStatus; ambiguous: boolean; hasKnowledgeIssue: boolean;
  overlapKind?: KnowledgeOverlap["kind"] | null; projectPath: string | null; createdAt: string; schemaVersion: number;
}
export interface LinkDiagnostics {
  notesAnalyzed: number; openSuggestions: number; directMentions: number; graphOpportunities: number; projectOpportunities: number;
  betterTargets: number; dismissed: number; inserted: number; candidatesCheckedThisProcess: number; deterministicFindings: number;
  modelClassifications: number; cacheHits: number; pendingJobs: number; failedJobs: number; bounded: boolean;
}
export interface LinkOpportunitySnapshot {
  schemaVersion: number; generatedAt: string; scope: { kind: "library" | "project" | "note"; path?: string; noteId?: string };
  suggestions: LinkSuggestion[]; diagnostics: LinkDiagnostics; degraded?: string;
}

export interface SmartLinkEvidence { links?: Map<string, Links>; issues?: KnowledgeIssue[]; overlaps?: KnowledgeOverlap[]; semantic?: Map<string, string[]>; }
type IndexedNote = LinkTarget & { markdown: string; aliases: string[]; headings: string[]; claims: string[]; projectPath: string | null; };

const generic = new Set(["app", "code", "data", "document", "file", "link", "note", "server", "service", "system", "test", "user"]);
const weakHeadings = new Set(["overview", "introduction", "notes", "summary", "references", "links", "resources"]);
const stop = new Set(["a", "an", "and", "are", "as", "at", "be", "by", "for", "from", "in", "is", "it", "of", "on", "or", "that", "the", "this", "to", "we", "with"]);
export function smartLinkHash(value: string) { let h = 2166136261; for (let i = 0; i < value.length; i++) { h ^= value.charCodeAt(i); h = Math.imul(h, 16777619); } return (h >>> 0).toString(36); }
function words(value: string) { return [...new Set(value.toLowerCase().replace(/[^a-z0-9.+#-]+/g, " ").split(/\s+/).filter((word) => word.length > 1 && !stop.has(word)))]; }
function normalized(value: string) { return value.toLowerCase().normalize("NFKC").replace(/\.md$/i, "").replace(/[^a-z0-9.+#]+/g, " ").trim().replace(/\s+/g, " "); }
function project(path: string) { const parts = path.replace(/\\/g, "/").split("/"); return parts.length > 1 ? parts.slice(0, Math.min(2, parts.length - 1)).join("/") : null; }
function parseMeta(markdown: string) {
  const match = markdown.replace(/\r\n?/g, "\n").match(/^---\s*\n([\s\S]*?)\n---\s*(?:\n|$)/); const values: Record<string, string> = {};
  for (const line of (match?.[1] ?? "").split("\n")) { const field = line.match(/^([A-Za-z][\w-]*):\s*(.*?)\s*$/); if (field) values[field[1].toLowerCase()] = field[2].replace(/^['"]|['"]$/g, "").trim(); }
  const aliases = String(values.aliases ?? values.alias ?? "").replace(/^\[|\]$/g, "").split(/[,|]/).map((v) => v.trim().replace(/^['"]|['"]$/g, "")).filter(Boolean);
  return { values, aliases, bodyStart: match?.[0].length ?? 0 };
}
function headings(markdown: string) { let fenced = false; return markdown.split(/\r?\n/).flatMap((line) => { if (/^\s*```/.test(line)) { fenced = !fenced; return []; } const match = !fenced ? line.match(/^#{1,4}\s+(.+?)\s*#*$/) : null; return match ? [match[1].trim()] : []; }); }
function indexNote(note: Note): IndexedNote {
  const meta = parseMeta(note.markdown); const claims = extractKnowledgeClaims(note).map((claim) => `${claim.normalizedSubject}|${claim.normalizedPredicate}|${claim.normalizedObject}`);
  return { noteId: note.id, revision: note.revision, contentHash: smartLinkHash(note.markdown), title: note.title, path: note.path, heading: null, noteType: String(meta.values.type ?? "").toLowerCase() || null, markdown: note.markdown, aliases: meta.aliases, headings: headings(note.markdown), claims, projectPath: project(note.path) };
}
function maskedProse(markdown: string) {
  const chars = [...markdown]; const blank = (from: number, to: number) => { for (let i = from; i < to; i++) if (chars[i] !== "\n") chars[i] = " "; };
  const meta = parseMeta(markdown); blank(0, meta.bodyStart); const patterns = [/```[\s\S]*?```/g, /`[^`\n]+`/g, /^\s*>.*$/gm, /\[[^\]\n]*\]\([^\)\n]+\)/g, /\[\[[^\]\n]+\]\]/g, /https?:\/\/\S+/g, /^\s*(?:at\s+\S+|\w+(?:Exception|Error):).+$/gm];
  for (const pattern of patterns) for (const match of markdown.matchAll(pattern)) blank(match.index!, match.index! + match[0].length);
  return chars.join("");
}
function excerpt(markdown: string, from: number, to: number) { return markdown.slice(Math.max(0, from - 90), Math.min(markdown.length, to + 120)).replace(/\s+/g, " ").trim().slice(0, 320); }
function pairKey(a: string, b: string) { return [a, b].sort().join("|"); }
export function linkSuggestionMarkdown(target: LinkTarget, visible: string) { const destination = target.title + (target.heading ? `#${target.heading}` : ""); return normalized(visible) === normalized(target.title) && !target.heading ? `[[${target.title}]]` : `[[${destination}|${visible}]]`; }
function exactExisting(source: IndexedNote, target: IndexedNote, links?: Links) {
  return Boolean(links?.outgoing.some((link) => link.targetId === target.noteId || normalized(link.target) === normalized(target.title) || normalized(link.targetPath ?? "") === normalized(target.path)));
}
function makeSuggestion(source: IndexedNote, target: IndexedNote, type: LinkSuggestionType, now: string, input: Partial<LinkSuggestion> & { signals: string[]; explanation: string }): LinkSuggestion {
  const range = input.sourceRange; const context = range ? source.markdown.slice(Math.max(0, range.from - 36), Math.min(source.markdown.length, range.to + 36)) : `${source.title}|${target.title}`;
  const fingerprint = `link-${smartLinkHash(`${source.noteId}:${source.contentHash}|${target.noteId}:${target.contentHash}|${range?.from ?? "note"}|${context}|${type}|v${SMART_LINK_SCHEMA_VERSION}`)}`;
  return { id: fingerprint, fingerprint, sourceNoteId: source.noteId, sourceRevision: source.revision, sourceHash: source.contentHash, sourceTitle: source.title, sourcePath: source.path,
    target, alternateTargets: input.alternateTargets ?? [], sourceHeading: input.sourceHeading ?? null, sourceExcerpt: input.sourceExcerpt ?? "", sourceRange: range,
    existingText: input.existingText, suggestedMarkdown: input.suggestedMarkdown, suggestionType: type, suggestedLinkKind: target.heading ? "wiki-heading" : "wiki",
    signals: input.signals, explanation: input.explanation, status: "open", ambiguous: Boolean(input.ambiguous), hasKnowledgeIssue: Boolean(input.hasKnowledgeIssue),
    overlapKind: input.overlapKind ?? null, projectPath: source.projectPath === target.projectPath ? source.projectPath : null, createdAt: now, schemaVersion: SMART_LINK_SCHEMA_VERSION };
}

export function analyzeSmartLinks(notes: Note[], scope: LinkOpportunitySnapshot["scope"], evidence: SmartLinkEvidence = {}, now = new Date().toISOString()): LinkOpportunitySnapshot {
  const boundedNotes = notes.slice(0, SMART_LINK_LIMITS.maxNotes).map(indexNote); const byId = new Map(boundedNotes.map((note) => [note.noteId, note]));
  const names = new Map<string, IndexedNote[]>(), tokenIndex = new Map<string, IndexedNote[]>();
  for (const note of boundedNotes) {
    for (const name of [note.title, ...note.aliases, ...note.headings.filter((h) => words(h).length >= 2 && !weakHeadings.has(normalized(h)))]) { const key = normalized(name); const existing = names.get(key) ?? []; if (key.length >= 4 && !generic.has(key) && !existing.some((value) => value.noteId === note.noteId)) names.set(key, [...existing, note]); }
    for (const token of [...words(note.title), ...note.headings.flatMap(words), ...note.claims.flatMap(words)].slice(0, 20)) tokenIndex.set(token, [...(tokenIndex.get(token) ?? []), note]);
  }
  const links = evidence.links ?? new Map<string, Links>(); const adjacency = new Map<string, Set<string>>(); const referrers = new Map<string, Set<string>>();
  for (const note of boundedNotes) { const outgoing = new Set((links.get(note.noteId)?.outgoing ?? []).flatMap((link) => link.targetId && byId.has(link.targetId) ? [link.targetId] : [])); adjacency.set(note.noteId, outgoing); for (const target of outgoing) referrers.set(target, new Set([...(referrers.get(target) ?? []), note.noteId])); }
  const overlaps = evidence.overlaps ?? analyzeOverlaps(notes, scope, now).findings; const overlapByPair = new Map(overlaps.map((item) => [pairKey(item.noteA.noteId, item.noteB.noteId), item]));
  const issues = evidence.issues ?? []; const issuePairs = new Set(issues.map((item) => pairKey(item.sourceA.noteId, item.sourceB.noteId)));
  const suggestions: LinkSuggestion[] = [], seen = new Set<string>(); let candidates = 0, bounded = notes.length > boundedNotes.length;
  const add = (item: LinkSuggestion) => { if (seen.has(item.fingerprint) || suggestions.filter((v) => v.sourceNoteId === item.sourceNoteId).length >= SMART_LINK_LIMITS.maxSuggestionsPerNote || suggestions.length >= SMART_LINK_LIMITS.maxSuggestions) { bounded = true; return; } seen.add(item.fingerprint); suggestions.push(item); };
  const sources = scope.kind === "note" ? boundedNotes.filter((note) => note.noteId === scope.noteId) : boundedNotes;
  for (const source of sources) {
    if (source.noteType === "template") continue; const prose = maskedProse(source.markdown); let mentions = 0;
    for (const [name, matches] of names) {
      if (mentions >= SMART_LINK_LIMITS.maxMentionsPerNote) { bounded = true; break; } const pattern = new RegExp(`(^|[^A-Za-z0-9])(${name.replace(/[.*+?^${}()|[\]\\]/g, "\\$&").replace(/\s+/g, "\\s+")})(?=$|[^A-Za-z0-9])`, "ig");
      for (const match of prose.matchAll(pattern)) {
        const from = match.index! + match[1].length, to = from + match[2].length; const viable = matches.filter((target) => target.noteId !== source.noteId && !exactExisting(source, target, links.get(source.noteId))).slice(0, SMART_LINK_LIMITS.maxTargetCandidatesPerMention); candidates += matches.length;
        if (!viable.length) continue; const target = viable.sort((a, b) => Number(b.projectPath === source.projectPath) - Number(a.projectPath === source.projectPath))[0]; const overlap = overlapByPair.get(pairKey(source.noteId, target.noteId));
        if (overlap && ["exact-duplicate", "near-duplicate", "duplicate-capture", "possibly-absorbed"].includes(overlap.kind)) continue;
        const visible = source.markdown.slice(from, to); const targetHeading = normalized(visible) !== normalized(target.title) && target.headings.some((h) => normalized(h) === normalized(visible)) ? visible : null; const resolved = { ...target, heading: targetHeading };
        add(makeSuggestion(source, resolved, "mention", now, { sourceRange: { from, to }, existingText: visible, suggestedMarkdown: linkSuggestionMarkdown(resolved, visible), sourceExcerpt: excerpt(source.markdown, from, to), alternateTargets: viable.slice(1), ambiguous: viable.length > 1,
          signals: [target.aliases.some((a) => normalized(a) === normalized(visible)) ? "Exact alias match" : targetHeading ? "Exact target heading match" : "Exact note title match", ...(target.projectPath === source.projectPath && source.projectPath ? ["Same project"] : [])],
          explanation: `${viable.length > 1 ? "This phrase matches more than one note. Choose the intended target before inserting a link." : targetHeading ? "This phrase exactly matches a heading in the target note." : "This phrase exactly matches an existing note title or alias."}${issuePairs.has(pairKey(source.noteId, target.noteId)) ? " Related, but these notes currently disagree." : ""}`, overlapKind: overlap?.kind, hasKnowledgeIssue: issuePairs.has(pairKey(source.noteId, target.noteId)) }));
        mentions++; if (mentions >= SMART_LINK_LIMITS.maxMentionsPerNote) break;
      }
    }
    const sourceTokens = [...words(source.title), ...source.headings.flatMap(words), ...source.claims.flatMap(words)].slice(0, 20);
    const semanticIds = (evidence.semantic?.get(source.noteId) ?? []).slice(0, SMART_LINK_LIMITS.maxSemanticNeighbors);
    const candidatePool = [...new Set([...semanticIds.flatMap((id) => byId.get(id) ? [byId.get(id)!] : []), ...sourceTokens.flatMap((token) => tokenIndex.get(token) ?? [])])].filter((target) => target.noteId !== source.noteId);
    const nearby = candidatePool.slice(0, SMART_LINK_LIMITS.maxCandidateNeighbors);
    if (candidatePool.length > nearby.length || (evidence.semantic?.get(source.noteId)?.length ?? 0) > semanticIds.length) bounded = true;
    const direct = adjacency.get(source.noteId) ?? new Set<string>();
    const graphNeighbors = [...direct].slice(0, SMART_LINK_LIMITS.maxGraphNeighbors);
    if (direct.size > graphNeighbors.length) bounded = true;
    const twoHop = new Set(graphNeighbors.flatMap((middle) => {
      const neighbors = [...(adjacency.get(middle) ?? [])];
      if (neighbors.length > SMART_LINK_LIMITS.maxGraphNeighbors) bounded = true;
      return neighbors.slice(0, SMART_LINK_LIMITS.maxGraphNeighbors);
    }).filter((id) => id !== source.noteId && !direct.has(id)));
    for (const target of nearby) {
      candidates++; if (exactExisting(source, target, links.get(source.noteId))) continue; const overlap = overlapByPair.get(pairKey(source.noteId, target.noteId)); if (overlap && ["exact-duplicate", "near-duplicate", "duplicate-capture", "possibly-absorbed"].includes(overlap.kind)) continue;
      const sharedClaims = source.claims.filter((claim) => target.claims.includes(claim)); const sharedTerms = words(source.title + " " + source.headings.join(" ")).filter((word) => words(target.title + " " + target.headings.join(" ")).includes(word));
      const commonRefs = [...(referrers.get(source.noteId) ?? [])].filter((id) => referrers.get(target.noteId)?.has(id)).length; const sameProject = Boolean(source.projectPath && source.projectPath === target.projectPath); const graphBridge = twoHop.has(target.noteId); const semanticNeighbor = semanticIds.includes(target.noteId);
      let type: LinkSuggestionType | null = null, explanation = "", signals: string[] = [];
      if (graphBridge && (sharedClaims.length || sharedTerms.length >= 1)) { type = "graph_bridge"; explanation = "These notes are connected through an authored neighbor and share specific knowledge."; signals = ["Two-hop authored graph path"]; }
      else if (commonRefs >= 2 && (sharedClaims.length || sharedTerms.length >= 2)) { type = "related"; explanation = `${commonRefs} other notes reference both notes, and their content shares specific concepts.`; signals = [`${commonRefs} shared referrers`]; }
      else if (semanticNeighbor && sameProject && (sharedClaims.length || sharedTerms.length >= 1)) { type = source.noteType === "question" ? "concept_reference" : "related"; explanation = source.noteType === "question" ? "A bounded semantic neighbor in the same project appears to explain this question's subject." : "A bounded semantic neighbor reinforces specific shared project knowledge."; signals = ["Strong semantic relation", "Same project"]; }
      else if (sameProject && (sharedClaims.length || sharedTerms.length >= 3)) { type = "project_relationship"; explanation = "These notes belong to the same project and describe connected project knowledge."; signals = ["Same project"]; }
      else if (["index", "moc", "project"].includes(target.noteType ?? "") && sameProject && sharedTerms.length >= 1) { type = "overview_relationship"; explanation = "This detailed note appears to belong to a broader project or index note."; signals = ["Project overview target"]; }
      if (!type || (source.noteType === "daily" && !sharedClaims.length && !graphBridge)) continue; if (sharedClaims.length) signals.push(`${sharedClaims.length} shared knowledge claim${sharedClaims.length === 1 ? "" : "s"}`); if (sharedTerms.length) signals.push(`Shared concepts: ${sharedTerms.slice(0, 3).join(", ")}`);
      add(makeSuggestion(source, target, type, now, { sourceExcerpt: source.headings.slice(0, 3).join(" · ") || source.title, signals, explanation: issuePairs.has(pairKey(source.noteId, target.noteId)) ? `${explanation} Related, but these notes currently disagree.` : explanation, hasKnowledgeIssue: issuePairs.has(pairKey(source.noteId, target.noteId)), overlapKind: overlap?.kind }));
    }
  }
  // A supersession issue can turn an authored link to the older note into a review-only better-target suggestion.
  for (const issue of issues.filter((item) => item.kind === "supersession" && item.staleClaimId)) {
    const stale = issue.sourceA.claimId === issue.staleClaimId ? issue.sourceA : issue.sourceB; const current = stale === issue.sourceA ? issue.sourceB : issue.sourceA; const target = byId.get(current.noteId); if (!target) continue;
    for (const source of sources.filter((note) => adjacency.get(note.noteId)?.has(stale.noteId))) {
      const old = byId.get(stale.noteId); if (!old) continue; const raw = links.get(source.noteId)?.outgoing.find((link) => link.targetId === old.noteId)?.raw; const from = raw ? source.markdown.indexOf(raw) : -1;
      add(makeSuggestion(source, target, "better_target", now, { sourceRange: from >= 0 && raw ? { from, to: from + raw.length } : undefined, existingText: raw, suggestedMarkdown: raw ? linkSuggestionMarkdown(target, raw.replace(/^\[\[|\]\]$/g, "").split("|").at(-1)!) : undefined, sourceExcerpt: from >= 0 && raw ? excerpt(source.markdown, from, from + raw.length) : source.title, signals: ["Current link targets likely superseded material", "Knowledge Issue supersession evidence"], explanation: `The current link points to ${old.title}; ${target.title} appears to contain the current documented view. Review before replacing it.`, hasKnowledgeIssue: true }));
    }
  }
  const priority: Record<LinkSuggestionType, number> = { better_target: 7, mention: 6, graph_bridge: 5, concept_reference: 4, related: 3, project_relationship: 2, overview_relationship: 1 };
  suggestions.sort((a, b) => priority[b.suggestionType] - priority[a.suggestionType] || a.sourcePath.localeCompare(b.sourcePath) || a.target.path.localeCompare(b.target.path));
  const scoped = scope.kind === "project" && scope.path ? suggestions.filter((item) => item.sourcePath === scope.path || item.sourcePath.startsWith(`${scope.path}/`)) : suggestions;
  return { schemaVersion: SMART_LINK_SCHEMA_VERSION, generatedAt: now, scope, suggestions: scoped, diagnostics: { notesAnalyzed: sources.length, openSuggestions: scoped.length, directMentions: scoped.filter((v) => v.suggestionType === "mention").length, graphOpportunities: scoped.filter((v) => v.suggestionType === "graph_bridge").length, projectOpportunities: scoped.filter((v) => v.suggestionType === "project_relationship" || v.suggestionType === "overview_relationship").length, betterTargets: scoped.filter((v) => v.suggestionType === "better_target").length, dismissed: 0, inserted: 0, candidatesCheckedThisProcess: candidates, deterministicFindings: scoped.length, modelClassifications: 0, cacheHits: 0, pendingJobs: 0, failedJobs: 0, bounded }, degraded: "Deterministic relationship analysis is active; no generation provider is required." };
}

export function applyLinkSuggestionDraft(markdown: string, revision: string, suggestion: LinkSuggestion) {
  if (revision !== suggestion.sourceRevision || smartLinkHash(markdown) !== suggestion.sourceHash) return { ok: false as const, reason: "Text changed since this suggestion was generated. Refresh suggestions." };
  if (!suggestion.sourceRange || !suggestion.existingText || !suggestion.suggestedMarkdown) return { ok: false as const, reason: "This relationship does not have a safe inline insertion point." };
  const { from, to } = suggestion.sourceRange; if (markdown.slice(from, to) !== suggestion.existingText) return { ok: false as const, reason: "Text changed since this suggestion was generated. Refresh suggestions." };
  return { ok: true as const, markdown: markdown.slice(0, from) + suggestion.suggestedMarkdown + markdown.slice(to), from, to, insert: suggestion.suggestedMarkdown };
}
