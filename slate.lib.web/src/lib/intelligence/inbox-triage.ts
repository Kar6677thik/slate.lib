import type { Note } from "@/lib/api/contracts";
import type { KnowledgeConcept } from "./concepts";
import type { KnowledgeGap } from "./knowledge-gaps";
import type { KnowledgeOverlap, OverlapKind } from "./overlap";
import type { LinkSuggestion } from "./smart-links";

export const INBOX_TRIAGE_SCHEMA_VERSION = 1;
export const INBOX_TRIAGE_LIMITS = {
  capturesPerPass: 30,
  candidateNotes: 240,
  relatedNotes: 8,
  concepts: 6,
  projects: 3,
  folders: 3,
  overlaps: 4,
  links: 4,
  gaps: 3,
  semanticSources: 8,
  semanticNeighbors: 8,
  concurrency: 4,
} as const;

export type InboxContentType = "Thought" | "Question" | "Idea" | "Decision" | "Reference" | "Learning Note" | "Project Note" | "Meeting / Discussion Note" | "Bug / Problem" | "Experiment" | "Task-like Thought" | "Unknown";
export type TriageConfidence = "strong" | "likely" | "possible";
export type InboxReviewState = "unprocessed" | "processed" | "deferred";
export type SuggestionReviewState = "accepted" | "dismissed" | "not-relevant";
export type InboxAction = "keep" | "move" | "convert-question" | "append" | "create-note" | "later" | "delete";

export interface InboxTriageRelatedNote { noteId: string; title: string; path: string; score: number; reasons: string[]; overlapKind?: OverlapKind; }
export interface InboxTriageSuggestion { id: string; action: InboxAction; label: string; explanation: string; targetNoteId?: string; destination?: string; fingerprint: string; }
export interface InboxTriageAnalysis {
  captureId: string; libraryId: string; captureHash: string; sourceFingerprint: string; schemaVersion: number;
  title: string; path: string; revision: string; content: string; createdAt: string | null;
  suggestedType: InboxContentType; confidence: TriageConfidence; signals: string[];
  possibleProjects: Array<{ path: string; score: number; explanation: string }>;
  possibleFolders: Array<{ path: string; score: number; explanation: string }>;
  concepts: Array<{ id: string; name: string; reason: string }>;
  relatedNotes: InboxTriageRelatedNote[];
  overlapFindings: Array<{ id: string; kind: OverlapKind; label: string; explanation: string; targetNoteId: string }>;
  linkTargets: Array<{ noteId: string; title: string; path: string; explanation: string }>;
  knowledgeGaps: Array<{ id: string; fingerprint: string; title: string; summary: string }>;
  suggestedActions: InboxTriageSuggestion[]; explanation: string;
}
export interface InboxTriageDiagnostics { inboxItems: number; analyzed: number; pending: number; failed: number; strongProjectSuggestions: number; overlapMatches: number; questionSuggestions: number; appendOpportunities: number; deterministicClassifications: number; optionalModelClassifications: number; cacheHits: number; boundsReached: boolean; }
export interface InboxTriageSnapshot { schemaVersion: number; generatedAt: string; items: InboxTriageAnalysis[]; diagnostics: InboxTriageDiagnostics; degraded?: string; }

const stop = new Set(["a","an","and","are","as","at","be","by","can","do","does","for","from","how","i","in","is","it","of","on","or","should","that","the","this","to","we","what","when","where","which","why","with","would"]);
export function inboxTriageHash(value: string) { let h = 2166136261; for (let i = 0; i < value.length; i++) { h ^= value.charCodeAt(i); h = Math.imul(h, 16777619); } return (h >>> 0).toString(36); }
function metadata(markdown: string) { const match = markdown.match(/^---\s*\r?\n([\s\S]*?)\r?\n---\s*(?:\r?\n|$)/); const fields: Record<string,string> = {}; for (const line of (match?.[1] ?? "").split(/\r?\n/)) { const pair = line.match(/^([A-Za-z][\w-]*):\s*(.*?)\s*$/); if (pair) fields[pair[1].toLowerCase()] = pair[2].replace(/^['"]|['"]$/g, ""); } return { fields, body: match ? markdown.slice(match[0].length) : markdown }; }
function prose(markdown: string) { return metadata(markdown).body.replace(/```[\s\S]*?```/g," ").replace(/`[^`\n]+`/g," ").replace(/^\s*>.*$/gm," ").replace(/^\s*(?:at\s+\S+|(?:\w*(?:Exception|Error)|Error):).+$/gm," ").replace(/^#{1,6}\s+/gm," ").trim(); }
function words(value: string) { return [...new Set(value.toLowerCase().replace(/https?:\/\/\S+/g," link ").replace(/[^a-z0-9.+#-]+/g," ").split(/\s+/).filter((word) => word.length > 2 && !stop.has(word)))]; }
function createdAt(note: Note) { const values = metadata(note.markdown).fields; for (const key of ["captured-at","created","date"]) if (values[key] && !Number.isNaN(Date.parse(values[key]))) return new Date(values[key]).toISOString(); return null; }
function isInbox(note: Note) { return /(^|\/)inbox\//i.test(note.path.replace(/\\/g,"/")); }
function project(path: string) { const parts = path.replace(/\\/g,"/").split("/").filter(Boolean); if (parts.length < 2 || parts[0].toLowerCase() === "inbox") return null; return parts.slice(0, Math.min(2, parts.length - 1)).join("/"); }
function folder(path: string) { const normalized = path.replace(/\\/g,"/"); const index = normalized.lastIndexOf("/"); return index > 0 ? normalized.slice(0,index) : null; }
function similarity(left: string[], right: string[]) { const set = new Set(right), common = left.filter((token) => set.has(token)).length; return common / Math.max(1, Math.sqrt(left.length * right.length)); }

export function classifyInboxCapture(note: Note): { type: InboxContentType; confidence: TriageConfidence; signals: string[] } {
  const meta = metadata(note.markdown), text = prose(note.markdown), first = text.split(/\r?\n/).find(Boolean)?.trim() ?? "";
  const found: Array<{ type: InboxContentType; weight: number; signal: string }> = [];
  const add = (type: InboxContentType, weight: number, signal: string) => found.push({ type, weight, signal });
  const explicit = String(meta.fields.type ?? "").toLowerCase();
  if (explicit === "question") add("Question", 5, "Explicit question metadata");
  if (explicit === "idea") add("Idea", 5, "Explicit idea metadata");
  if (/\?$/.test(first) || /^(?:how|why|what|when|where|who|can|could|should|would|is|are|do|does)\b/i.test(first)) add("Question", 4, "The capture is phrased as a direct question");
  if (/(?:\bdecision:|\bdecided\b|\bwe will use\b|\bchosen\b|\bgo with\b)/i.test(text)) add("Decision", 4, "Decision language appears in the capture");
  if (/\b(?:idea:|could build|what if)\b/i.test(text) || /^maybe\b/i.test(first)) add("Idea", 3, "Exploratory idea language appears in the capture");
  if (/\b(?:bug|broken|failed|failure|exception|unexpected|investigate|not working)\b/i.test(text)) add("Bug / Problem", 3, "Problem or investigation language appears in the capture");
  const urls = text.match(/https?:\/\/\S+/g)?.length ?? 0;
  if (urls >= 2 || (urls === 1 && words(text).length < 35)) add("Reference", 4, "The capture is primarily a reference or URL");
  if (/^(?:todo|remember to|need to|follow up|check|fix|add|update|review|write|call)\b/i.test(first)) add("Task-like Thought", 2, "The opening phrase appears actionable");
  if (/\b(?:experiment|hypothesis|try measuring|test whether)\b/i.test(text)) add("Experiment", 3, "Experiment language appears in the capture");
  if (/\b(?:learned|today i learned|lesson|understand)\b/i.test(text)) add("Learning Note", 2, "Learning language appears in the capture");
  found.sort((a,b) => b.weight-a.weight);
  if (!found.length) return { type: text.split(/\s+/).filter(Boolean).length >= 18 ? "Thought" : "Unknown", confidence: "possible", signals: ["No strong deterministic type signal"] };
  const top = found[0], tied = found.filter((item) => item.weight === top.weight && item.type !== top.type);
  if (tied.length) return { type: "Unknown", confidence: "possible", signals: [top.signal, ...tied.map((item) => item.signal)] };
  return { type: top.type, confidence: top.weight >= 4 ? "strong" : top.weight >= 3 ? "likely" : "possible", signals: found.filter((item) => item.type === top.type).map((item) => item.signal) };
}

export function analyzeInboxCapture(input: { libraryId: string; capture: Note; notes: Note[]; concepts?: KnowledgeConcept[]; overlaps?: KnowledgeOverlap[]; links?: LinkSuggestion[]; gaps?: KnowledgeGap[]; semanticNoteIds?: string[] }): InboxTriageAnalysis {
  const { capture } = input, captureHash = inboxTriageHash(capture.markdown), captureWords = words(`${capture.title} ${prose(capture.markdown)}`), classification = classifyInboxCapture(capture);
  const overlapItems = (input.overlaps ?? []).filter((item) => item.noteA.noteId === capture.id || item.noteB.noteId === capture.id).slice(0, INBOX_TRIAGE_LIMITS.overlaps);
  const overlapByTarget = new Map(overlapItems.map((item) => [item.noteA.noteId === capture.id ? item.noteB.noteId : item.noteA.noteId, item]));
  const linkItems = (input.links ?? []).filter((item) => item.sourceNoteId === capture.id).slice(0, INBOX_TRIAGE_LIMITS.links);
  const linkByTarget = new Map(linkItems.map((item) => [item.target.noteId, item]));
  const conceptItems = (input.concepts ?? []).filter((concept) => concept.members.some((member) => member.noteId === capture.id) || words(concept.canonicalName).every((token) => captureWords.includes(token))).sort((a,b) => b.sourceCount-a.sourceCount).slice(0, INBOX_TRIAGE_LIMITS.concepts);
  const conceptTargets = new Map<string,string[]>();
  for (const concept of conceptItems) for (const member of concept.members) if (member.noteId !== capture.id) conceptTargets.set(member.noteId, [...(conceptTargets.get(member.noteId) ?? []), concept.canonicalName]);
  const related = input.notes.filter((note) => note.id !== capture.id && !isInbox(note)).map((note) => {
    const lexical = similarity(captureWords, words(`${note.title} ${prose(note.markdown).slice(0,4000)}`)); const overlap = overlapByTarget.get(note.id); const link = linkByTarget.get(note.id); const concepts = conceptTargets.get(note.id) ?? []; const semantic = input.semanticNoteIds?.includes(note.id) ?? false; let score = lexical * 60 + concepts.length * 12 + (link ? 25 : 0) + (overlap ? 40 : 0) + (semantic ? 28 : 0); const reasons: string[] = [];
    if (overlap) reasons.push(overlap.kind === "exact-duplicate" || overlap.kind === "duplicate-capture" ? "Possible duplicate" : overlap.kind === "near-duplicate" ? "Near duplicate" : "Partial overlap");
    if (link) reasons.push("Smart Link target"); if (semantic) reasons.push("Semantic relation"); if (concepts.length) reasons.push(`Related concept${concepts.length === 1 ? "" : "s"}: ${concepts.slice(0,2).join(", ")}`); if (lexical >= .34) reasons.push("Exact topic match"); else if (lexical >= .18) reasons.push("Related topic");
    const noteProject = project(note.path); if (noteProject && concepts.some((name) => note.path.toLowerCase().includes(name.toLowerCase()))) { score += 8; reasons.push("Same project context"); }
    return { noteId: note.id, title: note.title, path: note.path, score: Math.round(score), reasons, ...(overlap ? { overlapKind: overlap.kind } : {}) };
  }).filter((item) => item.score >= 10 && item.reasons.length).sort((a,b) => b.score-a.score || a.title.localeCompare(b.title)).slice(0, INBOX_TRIAGE_LIMITS.relatedNotes);
  const projectScores = new Map<string,{ score:number; notes:number }>(); const folderScores = new Map<string,{ score:number; notes:number }>();
  for (const item of related.slice(0,5)) { const p = project(item.path), f = folder(item.path); if (p) projectScores.set(p,{ score:(projectScores.get(p)?.score ?? 0)+item.score, notes:(projectScores.get(p)?.notes ?? 0)+1 }); if (f) folderScores.set(f,{ score:(folderScores.get(f)?.score ?? 0)+item.score, notes:(folderScores.get(f)?.notes ?? 0)+1 }); }
  const possibleProjects = [...projectScores].filter(([,v]) => v.notes >= 2 || v.score >= 70).sort((a,b) => b[1].score-a[1].score).slice(0,INBOX_TRIAGE_LIMITS.projects).map(([path,value]) => ({ path, score:value.score, explanation:`${value.notes} strong related note${value.notes === 1 ? "" : "s"} ${value.notes === 1 ? "is" : "are"} in this project` }));
  const possibleFolders = [...folderScores].filter(([,v]) => v.notes >= 2 || v.score >= 75).sort((a,b) => b[1].score-a[1].score).slice(0,INBOX_TRIAGE_LIMITS.folders).map(([path,value]) => ({ path, score:value.score, explanation:`Existing folder used by ${value.notes} related note${value.notes === 1 ? "" : "s"}` }));
  const gapItems = (input.gaps ?? []).map((gap) => ({ gap, score: similarity(captureWords, words(`${gap.title} ${gap.summary} ${gap.conceptName ?? ""}`)) + (gap.sourceConceptIds.some((id) => conceptItems.some((concept) => concept.id === id)) ? .5 : 0) })).filter((item) => item.score >= .22).sort((a,b) => b.score-a.score).slice(0,INBOX_TRIAGE_LIMITS.gaps).map(({gap}) => ({ id:gap.id, fingerprint:gap.fingerprint, title:gap.title, summary:gap.summary }));
  const actions: Omit<InboxTriageSuggestion,"fingerprint">[] = [];
  if (classification.type === "Question" && classification.confidence !== "possible") actions.push({ id:"convert-question", action:"convert-question", label:"Convert to Question", explanation:"The capture is phrased as a direct question." });
  const strongest = related[0]; if (strongest && (strongest.score >= 30 || strongest.overlapKind)) actions.push({ id:`append:${strongest.noteId}`, action:"append", label:`Append to ${strongest.title}`, targetNoteId:strongest.noteId, explanation:strongest.reasons.join(" · ") });
  if (possibleFolders[0]) actions.push({ id:`move:${possibleFolders[0].path}`, action:"move", label:`Move to ${possibleFolders[0].path}`, destination:possibleFolders[0].path, explanation:possibleFolders[0].explanation });
  actions.push({ id:"keep", action:"keep", label:"Keep as Note", explanation:"Keep the capture intact and mark it reviewed." }, { id:"create-note", action:"create-note", label:"Create New Note", destination:possibleFolders[0]?.path, explanation:"Create a separate note while preserving the original capture." });
  const sourceFingerprint = inboxTriageHash(related.map((item) => `${item.noteId}:${item.score}`).join("|") + overlapItems.map((item) => item.fingerprint).join("|") + conceptItems.map((item) => item.contentHash).join("|"));
  const suggestedActions = actions.map((action) => ({ ...action, fingerprint:`v${INBOX_TRIAGE_SCHEMA_VERSION}:${captureHash}:${inboxTriageHash(`${action.id}|${action.targetNoteId ?? ""}|${action.destination ?? ""}|${sourceFingerprint}`)}` }));
  return { captureId:capture.id, libraryId:input.libraryId, captureHash, sourceFingerprint, schemaVersion:INBOX_TRIAGE_SCHEMA_VERSION, title:capture.title, path:capture.path, revision:capture.revision, content:prose(capture.markdown), createdAt:createdAt(capture), suggestedType:classification.type, confidence:classification.confidence, signals:classification.signals, possibleProjects, possibleFolders, concepts:conceptItems.map((concept) => ({ id:concept.id, name:concept.canonicalName, reason:concept.members.some((member) => member.noteId === capture.id) ? "Found in this capture" : "Named in the capture" })), relatedNotes:related, overlapFindings:overlapItems.map((item) => ({ id:item.id, kind:item.kind, label:item.label, explanation:item.explanation, targetNoteId:item.noteA.noteId === capture.id ? item.noteB.noteId : item.noteA.noteId })), linkTargets:linkItems.map((item) => ({ noteId:item.target.noteId, title:item.target.title, path:item.target.path, explanation:item.explanation })), knowledgeGaps:gapItems, suggestedActions, explanation:possibleProjects[0] ? `${classification.type} capture with related material in ${possibleProjects[0].path}.` : `${classification.type} capture; review the related notes before choosing a destination.` };
}

export function orderInboxTriage(items: InboxTriageAnalysis[]) { const rank = (item: InboxTriageAnalysis) => item.overlapFindings.some((finding) => ["exact-duplicate","duplicate-capture","near-duplicate"].includes(finding.kind)) ? 0 : item.suggestedActions.some((action) => action.action === "append") ? 1 : item.suggestedType === "Question" && item.confidence !== "possible" ? 2 : item.possibleFolders.length ? 3 : item.suggestedType === "Unknown" ? 5 : 4; return [...items].sort((a,b) => rank(a)-rank(b) || (a.createdAt ?? "").localeCompare(b.createdAt ?? "") || a.title.localeCompare(b.title)); }
