"use client";

import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Check, ChevronRight, Copy, FileDiff, Files, History, Layers3, MessageCircleQuestion, Search, ShieldQuestion, X } from "lucide-react";
import { useApi } from "@/lib/auth/context";
import { useWorkspace } from "@/features/notes/workspace-context";
import { openAskSlate } from "@/components/ask/ask-slate";
import { Button } from "@/components/ui/button";
import { ErrorMessage, Loading } from "@/components/common/primitives";
import { saveManualMergeDraft, useOverlapReviews } from "@/lib/storage/overlap-reviews";
import type { KnowledgeOverlap, OverlapKind, OverlapReviewState, OverlapSection, OverlapSnapshot } from "@/lib/intelligence/overlap";

const labels: Record<OverlapKind, string> = { "exact-duplicate": "Exact duplicates", "near-duplicate": "Near duplicates", "partial-overlap": "Partial overlaps", "possibly-absorbed": "Possibly absorbed", fragmented: "Consolidation opportunities", "duplicate-capture": "Duplicate captures" };
type CompareMode = "overview" | "shared" | "only-a" | "only-b" | "full";

function SectionList({ title, sections }: { title: string; sections: OverlapSection[] }) {
  return <section className="overlap-section-list" aria-label={title}><h3>{title}</h3>{sections.length ? sections.map((section) => <article key={`${section.hash}-${section.heading}`}><strong>{section.heading}</strong><p>{section.text.slice(0, 700)}</p></article>) : <p className="project-empty">No substantial sections in this view.</p>}</section>;
}

function NoteCard({ label, note, open }: { label: string; note: KnowledgeOverlap["noteA"]; open: () => void }) {
  return <section className="overlap-note" aria-label={`${label}: ${note.title}`}><header><span>{label}</span><small>{note.timestamp ? new Date(note.timestamp).toLocaleDateString() : "Date not documented"}</small></header><h3>{note.title}</h3><p className="knowledge-path">{note.path}</p><p>{note.headings.slice(0, 5).join(" · ") || "No named sections"}</p><Button variant="outline" size="sm" onClick={open}>Open full note</Button></section>;
}

function Compare({ item, onClose, setReview }: { item: KnowledgeOverlap; onClose: () => void; setReview: (state: OverlapReviewState) => void }) {
  const workspace = useWorkspace(); const [mode, setMode] = useState<CompareMode>("overview"); const [draft, setDraft] = useState<string | null>(null);
  const ask = (question = "How are these notes different? Identify shared knowledge and what is unique to each, using only these sources.") => openAskSlate({ scope: "selected", noteIds: [item.noteA.noteId, item.noteB.noteId], question });
  const createDraft = () => {
    const unique = item.uniqueSectionsB.map((section) => `## ${section.heading}\n\n${section.text}`).join("\n\n");
    const value = `${item.noteA.normalizedContent}\n\n<!-- Manual merge plan from ${item.noteB.path}; review before saving. -->\n\n${unique}`.trim();
    saveManualMergeDraft(workspace.scope, { fingerprint: item.fingerprint, targetNoteId: item.noteA.noteId, sourceNoteId: item.noteB.noteId, markdown: value, createdAt: new Date().toISOString() }); setDraft(value);
  };
  return <aside className="overlap-comparison" aria-label="Knowledge overlap comparison">
    <header className="knowledge-comparison-header"><div><span>{item.label}</span><h2>{item.noteA.title} and {item.noteB.title}</h2><p>{item.explanation}</p></div><Button variant="ghost" size="sm" onClick={onClose} aria-label="Close overlap comparison"><X size={17} /></Button></header>
    <div className="overlap-tabs" role="tablist" aria-label="Comparison view">{(["overview", "shared", "only-a", "only-b", "full"] as CompareMode[]).map((value) => <button role="tab" aria-selected={mode === value} key={value} onClick={() => setMode(value)}>{value === "only-a" ? "Only A" : value === "only-b" ? "Only B" : value[0].toUpperCase() + value.slice(1)}</button>)}</div>
    {mode === "overview" && <><div className="knowledge-sources"><NoteCard label="Note A" note={item.noteA} open={() => workspace.open(item.noteA.noteId)} /><NoteCard label="Note B" note={item.noteB} open={() => workspace.open(item.noteB.noteId)} /></div><div className="overlap-overview"><SectionList title="Shared knowledge" sections={item.sharedSections} /><SectionList title="Unique to A" sections={item.uniqueSectionsA} /><SectionList title="Unique to B" sections={item.uniqueSectionsB} /></div></>}
    {mode === "shared" && <SectionList title="Shared knowledge" sections={item.sharedSections} />}
    {mode === "only-a" && <SectionList title="Unique to A" sections={item.uniqueSectionsA} />}
    {mode === "only-b" && <SectionList title="Unique to B" sections={item.uniqueSectionsB} />}
    {mode === "full" && <div className="knowledge-sources"><SectionList title={`Full note A · ${item.noteA.title}`} sections={item.noteA.sections} /><SectionList title={`Full note B · ${item.noteB.title}`} sections={item.noteB.sections} /></div>}
    {item.hasKnowledgeIssue && <button className="overlap-linked-issue" onClick={() => workspace.openKnowledgeIssues({ kind: "note", noteId: item.noteA.noteId })}><ShieldQuestion size={16} /><span><strong>Also has a Knowledge Issue</strong><small>Review the conflicting claims before planning consolidation.</small></span><ChevronRight size={15} /></button>}
    <details className="merge-preview"><summary>Manual merge preview</summary><p>Target: {item.noteA.title}. Source-only sections from {item.noteB.title} are included; shared sections remain excluded from the plan.</p><div><Button variant="outline" size="sm" onClick={() => navigator.clipboard.writeText(item.uniqueSectionsB.map((section) => `## ${section.heading}\n\n${section.text}`).join("\n\n"))}><Copy size={15} />Copy unique sections</Button><Button variant="outline" size="sm" onClick={createDraft}><FileDiff size={15} />Create manual merge draft</Button></div>{draft && <label><span>Browser-local editable draft</span><textarea value={draft} onChange={(event) => setDraft(event.target.value)} rows={12} /><small>This draft has not been saved to either note.</small></label>}</details>
    <div className="knowledge-sticky-actions"><Button variant="outline" onClick={() => workspace.open(item.noteA.noteId)}><Files size={16} />Open</Button><Button variant="outline" onClick={() => ask()}><MessageCircleQuestion size={16} />Ask</Button><Button variant="outline" onClick={() => workspace.openEvolution({ kind: "selected", noteIds: [item.noteA.noteId, item.noteB.noteId], topic: item.noteA.title })}><History size={16} />Evolution</Button><Button variant="outline" onClick={() => setReview("keep-separate")}><Layers3 size={16} />Keep separate</Button><Button onClick={() => setReview("resolved")}><Check size={16} />Resolve</Button></div>
  </aside>;
}

function matches(item: KnowledgeOverlap, query: string) { const value = `${item.label} ${item.explanation} ${item.noteA.title} ${item.noteA.path} ${item.noteB.title} ${item.noteB.path} ${item.sharedSections.map((section) => section.text).join(" ")}`.toLowerCase(); return value.includes(query.toLowerCase()); }

export function OverlapProjectSummary({ path }: { path: string }) {
  const api = useApi(), workspace = useWorkspace(); const scope: OverlapSnapshot["scope"] = { kind: "project", path };
  const query = useQuery({ queryKey: ["knowledge-overlap", scope], queryFn: ({ signal }) => api.knowledgeOverlap(scope, signal), staleTime: 30_000 });
  if (query.isPending) return <p className="project-empty">Checking project overlap…</p>;
  if (!query.data || query.error || !query.data.findings.length) return null;
  return <div className="project-knowledge-summary"><p>{query.data.findings.length} overlap or consolidation item{query.data.findings.length === 1 ? "" : "s"} may need review.</p>{query.data.findings.slice(0, 3).map((item) => <button key={item.fingerprint} onClick={() => workspace.openKnowledgeOverlap(scope)}><Layers3 size={15} /><span><strong>{item.label}</strong><small>{item.noteA.title} · {item.noteB.title}</small></span><ChevronRight size={15} /></button>)}<Button variant="outline" size="sm" onClick={() => workspace.openKnowledgeOverlap(scope)}>Review overlap</Button></div>;
}

export function InboxOverlapIndicator() {
  const api = useApi(), workspace = useWorkspace(); const scope: OverlapSnapshot["scope"] = { kind: "project", path: "inbox" };
  const query = useQuery({ queryKey: ["knowledge-overlap", scope], queryFn: ({ signal }) => api.knowledgeOverlap(scope, signal), staleTime: 30_000 });
  const item = query.data?.findings.find((finding) => finding.state === "open"); if (!item) return null;
  return <button className="inbox-overlap-indicator" onClick={() => workspace.openKnowledgeOverlap(scope)}><Layers3 size={16} /><span><strong>Similar knowledge exists</strong><small>{item.noteA.title} and {item.noteB.title}</small></span><ChevronRight size={15} /></button>;
}

export function OverlapWorkspace({ scope }: { scope: OverlapSnapshot["scope"] }) {
  const api = useApi(), workspace = useWorkspace(), reviews = useOverlapReviews(workspace.scope);
  const [kind, setKind] = useState<OverlapKind | "all">("all"), [state, setState] = useState<OverlapReviewState | "all">("open"), [search, setSearch] = useState(""), [selected, setSelected] = useState<string | null>(null);
  const query = useQuery({ queryKey: ["knowledge-overlap", scope], queryFn: ({ signal }) => api.knowledgeOverlap(scope, signal), staleTime: 30_000 });
  const findings = useMemo(() => (query.data?.findings ?? []).map((item) => ({ ...item, state: reviews.reviews[item.fingerprint]?.state ?? item.state })), [query.data, reviews.reviews]);
  const visible = findings.filter((item) => (kind === "all" || item.kind === kind) && (state === "all" || item.state === state) && matches(item, search)); const active = findings.find((item) => item.fingerprint === selected) ?? null;
  if (query.isPending) return <Loading label="Comparing current knowledge…" />;
  if (query.error || !query.data) return <ErrorMessage error={query.error ?? new Error("Overlap analysis is unavailable")} retry={() => query.refetch()} />;
  return <div className="knowledge-workspace overlap-workspace scroll-area"><header className="knowledge-hero"><div><span className="eyebrow">Knowledge organization</span><h1>Knowledge Overlap</h1><p>Review exact copies, shared sections, absorbed material, and possible consolidation without changing canonical notes.</p></div><Button variant="outline" onClick={() => query.refetch()}>Check again</Button></header>
    <div className="knowledge-counts" aria-label="Overlap summary"><div><strong>{query.data.diagnostics.exactDuplicates}</strong><span>exact duplicates</span></div><div><strong>{query.data.diagnostics.nearDuplicates}</strong><span>near duplicates</span></div><div><strong>{query.data.diagnostics.partialOverlaps}</strong><span>partial overlaps</span></div><div><strong>{query.data.diagnostics.possiblyAbsorbed}</strong><span>possibly absorbed</span></div></div>
    <div className="knowledge-controls"><label className="knowledge-search"><Search size={16} /><span className="sr-only">Search overlap findings</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search notes, paths, shared knowledge…" /></label><label><span>Relationship</span><select value={kind} onChange={(event) => setKind(event.target.value as typeof kind)}><option value="all">All relationships</option>{Object.entries(labels).map(([value, text]) => <option key={value} value={value}>{text}</option>)}</select></label><label><span>Review state</span><select value={state} onChange={(event) => setState(event.target.value as typeof state)}><option value="open">Open</option><option value="keep-separate">Keep separate</option><option value="resolved">Resolved</option><option value="dismissed">Dismissed</option><option value="all">All states</option></select></label></div>
    {query.data.degraded && <p className="knowledge-degraded">{query.data.degraded}</p>}
    <div className={`knowledge-layout ${active ? "has-comparison" : ""}`}><section className="knowledge-list" aria-label="Knowledge overlap findings">{visible.map((item) => <article key={item.fingerprint} className="knowledge-issue"><button className="knowledge-issue-main" onClick={() => setSelected(item.fingerprint)}><span className="knowledge-kind">{item.label}</span><strong>{item.noteA.title} and {item.noteB.title}</strong><p>{item.explanation}</p><small>{item.sharedSections.length} shared · {item.uniqueSectionsA.length} unique to A · {item.uniqueSectionsB.length} unique to B</small></button><div><Button variant="ghost" size="sm" onClick={() => workspace.open(item.noteA.noteId)}>A</Button><Button variant="ghost" size="sm" onClick={() => workspace.open(item.noteB.noteId)}>B</Button><Button variant="ghost" size="sm" onClick={() => setSelected(item.fingerprint)}><FileDiff size={15} />Compare</Button></div></article>)}{!visible.length && <div className="knowledge-empty"><Layers3 size={24} /><h2>No overlap in this view</h2><p>Similarity alone does not create a finding. Try another relationship or review state.</p></div>}</section>{active && <Compare item={active} onClose={() => setSelected(null)} setReview={(next) => { reviews.setReview(active.fingerprint, next); if (next !== "open") setSelected(null); }} />}</div>
    <details className="knowledge-diagnostics"><summary>Analysis diagnostics</summary><dl>{Object.entries(query.data.diagnostics).map(([name, value]) => <div key={name}><dt>{name.replace(/([A-Z])/g, " $1")}</dt><dd>{String(value)}</dd></div>)}</dl></details>
  </div>;
}
