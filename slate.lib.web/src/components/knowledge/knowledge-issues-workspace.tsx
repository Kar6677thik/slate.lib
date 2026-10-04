"use client";

import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Check, ChevronRight, CircleHelp, Clock3, FileDiff, History, MessageCircleQuestion, Search, ShieldQuestion, X } from "lucide-react";
import { useApi } from "@/lib/auth/context";
import { useWorkspace } from "@/features/notes/workspace-context";
import { useKnowledgeReviews } from "@/lib/storage/knowledge-reviews";
import { openAskSlate } from "@/components/ask/ask-slate";
import { Button } from "@/components/ui/button";
import { ErrorMessage, Loading } from "@/components/common/primitives";
import type { KnowledgeIssue, KnowledgeSnapshot, ReviewState } from "@/lib/intelligence/knowledge-issues";

const kindLabel: Record<KnowledgeIssue["kind"], string> = {
  reversal: "Current contradictions", value: "Version / config conflicts", version: "Version / config conflicts",
  status: "Current contradictions", architecture: "Architecture", decision: "Decisions", requirement: "Requirements",
  "answered-question": "Possible answers found", supersession: "Likely superseded",
};
const evidenceLabel: Record<KnowledgeIssue["evidence"], string> = {
  "documented-reversal": "Documented reversal", "strong-conflict": "Strong conflict", "possible-conflict": "Possible conflict",
  "likely-superseded": "Likely superseded", "possible-stale": "Possibly stale",
};

function Source({ label, issue, side, onOpen }: { label: string; issue: KnowledgeIssue; side: "sourceA" | "sourceB"; onOpen: (id: string) => void }) {
  const source = issue[side];
  return <section className="knowledge-source" aria-label={`${label}: ${source.title}`}>
    <header><span>{label}</span><small>{source.current ? "Current" : "Historical"}</small></header>
    <h3>{source.title}</h3>
    <p className="knowledge-path">{source.path}{source.heading ? ` · ${source.heading}` : ""}</p>
    <blockquote>{source.text}</blockquote>
    <footer><span>{source.timestamp ? new Date(source.timestamp).toLocaleDateString() : "Date not documented"}</span><Button variant="outline" size="sm" onClick={() => onOpen(source.noteId)}>Open source</Button></footer>
  </section>;
}

function IssueComparison({ issue, setReview, onClose }: { issue: KnowledgeIssue; setReview: (state: ReviewState) => void; onClose: () => void }) {
  const workspace = useWorkspace();
  const ask = () => openAskSlate({ scope: "selected", noteIds: [issue.sourceA.noteId, issue.sourceB.noteId], question: "Explain this knowledge issue using only these two sources. Distinguish current conflict from normal historical change." });
  return <aside className="knowledge-comparison" aria-label="Knowledge issue comparison">
    <header className="knowledge-comparison-header">
      <div><span>{evidenceLabel[issue.evidence]}</span><h2>{issue.title}</h2><p>{issue.explanation}</p></div>
      <Button variant="ghost" size="sm" onClick={onClose} aria-label="Close comparison"><X size={17} /></Button>
    </header>
    <div className="knowledge-sources">
      <Source label="Source A" issue={issue} side="sourceA" onOpen={workspace.open} />
      <Source label="Source B" issue={issue} side="sourceB" onOpen={workspace.open} />
    </div>
    <div className="knowledge-sticky-actions">
      <Button variant="outline" onClick={ask}><MessageCircleQuestion size={16} />Ask</Button>
      <Button variant="outline" onClick={() => workspace.openEvolution({ kind: "selected", noteIds: [issue.sourceA.noteId, issue.sourceB.noteId], topic: issue.sourceA.normalizedSubject })}><History size={16} />Evolution</Button>
      <Button variant="outline" onClick={() => setReview("dismissed")}><X size={16} />Dismiss</Button>
      <Button onClick={() => setReview("resolved")}><Check size={16} />Resolve</Button>
    </div>
  </aside>;
}

function matches(issue: KnowledgeIssue, query: string) {
  if (!query) return true;
  const value = `${issue.title} ${issue.explanation} ${issue.sourceA.title} ${issue.sourceA.path} ${issue.sourceA.text} ${issue.sourceA.normalizedSubject} ${issue.sourceB.title} ${issue.sourceB.path} ${issue.sourceB.text} ${issue.sourceB.normalizedSubject}`.toLowerCase();
  return value.includes(query.toLowerCase());
}

export function KnowledgeIssueSummary({ scope }: { scope: KnowledgeSnapshot["scope"] }) {
  const api = useApi(); const workspace = useWorkspace();
  const query = useQuery({ queryKey: ["knowledge-issues", scope], queryFn: ({ signal }) => api.knowledgeIssues(scope, signal), staleTime: 30_000 });
  if (query.isPending) return <p className="project-empty">Checking project knowledge…</p>;
  if (!query.data || query.error) return <p className="project-empty">Knowledge analysis is unavailable.</p>;
  const open = query.data.issues.filter((issue) => issue.state === "open");
  return <div className="project-knowledge-summary">
    <p>{open.length ? `${open.length} issue${open.length === 1 ? "" : "s"} may need review.` : "No deterministic project issues were found."}</p>
    {open.slice(0, 3).map((issue) => <button key={issue.fingerprint} onClick={() => workspace.openKnowledgeIssues(scope)}><ShieldQuestion size={15} /><span><strong>{issue.title}</strong><small>{issue.sourceA.title} · {issue.sourceB.title}</small></span><ChevronRight size={15} /></button>)}
    <Button variant="outline" size="sm" onClick={() => workspace.openKnowledgeIssues(scope)}>Review knowledge issues</Button>
  </div>;
}

export function KnowledgeIssuesWorkspace({ scope }: { scope: KnowledgeSnapshot["scope"] }) {
  const api = useApi(); const workspace = useWorkspace(); const reviewStore = useKnowledgeReviews(workspace.scope);
  const [state, setState] = useState<"open" | "resolved" | "dismissed" | "all">("open");
  const [kind, setKind] = useState<KnowledgeIssue["kind"] | "all">("all");
  const [currentOnly, setCurrentOnly] = useState(false); const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<string | null>(null);
  const query = useQuery({ queryKey: ["knowledge-issues", scope], queryFn: ({ signal }) => api.knowledgeIssues(scope, signal), staleTime: 30_000 });
  const issues = useMemo(() => (query.data?.issues ?? []).map((issue) => ({ ...issue, state: reviewStore.reviews[issue.fingerprint]?.state ?? issue.state })), [query.data, reviewStore.reviews]);
  const visible = issues.filter((issue) => (state === "all" || issue.state === state || (state === "open" && issue.state === "snoozed")) && (kind === "all" || issue.kind === kind) && (!currentOnly || issue.currentCurrent) && matches(issue, search));
  const active = issues.find((issue) => issue.fingerprint === selected) ?? null;
  const update = (issue: KnowledgeIssue, next: ReviewState) => { reviewStore.setReview(issue.fingerprint, next); if (next !== "open") setSelected(null); };
  if (query.isPending) return <Loading label="Reviewing current knowledge…" />;
  if (query.error || !query.data) return <ErrorMessage error={query.error ?? new Error("Knowledge analysis is unavailable")} retry={() => query.refetch()} />;
  const counts = {
    conflicts: issues.filter((issue) => ["reversal", "value", "version", "status", "architecture", "decision", "requirement"].includes(issue.kind) && issue.state === "open").length,
    superseded: issues.filter((issue) => issue.kind === "supersession" && issue.state === "open").length,
    answers: issues.filter((issue) => issue.kind === "answered-question" && issue.state === "open").length,
  };
  return <div className="knowledge-workspace scroll-area">
    <header className="knowledge-hero">
      <div><span className="eyebrow">Internal consistency</span><h1>Knowledge Issues</h1><p>Review places where your current Slate sources differ, appear superseded, or may already answer an open question.</p></div>
      <Button variant="outline" onClick={() => query.refetch()}><Clock3 size={16} />Check again</Button>
    </header>
    <div className="knowledge-counts" aria-label="Knowledge issue summary">
      <div><strong>{counts.conflicts}</strong><span>current conflicts</span></div><div><strong>{counts.superseded}</strong><span>likely superseded</span></div><div><strong>{counts.answers}</strong><span>possible answers found</span></div>
    </div>
    <div className="knowledge-controls">
      <label className="knowledge-search"><Search size={16} /><span className="sr-only">Search knowledge issues</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search sources, paths, claims…" /></label>
      <label><span>Type</span><select value={kind} onChange={(event) => setKind(event.target.value as typeof kind)}><option value="all">All types</option>{Object.entries(kindLabel).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
      <label><span>Review state</span><select value={state} onChange={(event) => setState(event.target.value as typeof state)}><option value="open">Open</option><option value="resolved">Resolved</option><option value="dismissed">Dismissed</option><option value="all">All states</option></select></label>
      <label className="knowledge-check"><input type="checkbox" checked={currentOnly} onChange={(event) => setCurrentOnly(event.target.checked)} />Current-current only</label>
    </div>
    {query.data.degraded && <p className="knowledge-degraded">{query.data.degraded}</p>}
    <div className={`knowledge-layout ${active ? "has-comparison" : ""}`}>
      <section className="knowledge-list" aria-label="Knowledge issues">
        {visible.map((issue) => <article key={issue.fingerprint} className="knowledge-issue">
          <button className="knowledge-issue-main" onClick={() => setSelected(issue.fingerprint)} aria-label={`Compare ${issue.sourceA.title} and ${issue.sourceB.title}`}>
            <span className="knowledge-kind">{evidenceLabel[issue.evidence]}</span><strong>{issue.title}</strong><p>{issue.explanation}</p>
            <small>{issue.sourceA.title} <span>and</span> {issue.sourceB.title}</small>
          </button>
          <div><Button variant="ghost" size="sm" onClick={() => workspace.open(issue.sourceA.noteId)}>A</Button><Button variant="ghost" size="sm" onClick={() => workspace.open(issue.sourceB.noteId)}>B</Button><Button variant="ghost" size="sm" onClick={() => setSelected(issue.fingerprint)}><FileDiff size={15} />Compare</Button></div>
        </article>)}
        {!visible.length && <div className="knowledge-empty"><CircleHelp size={24} /><h2>No issues in this view</h2><p>Try another type or review state. Age alone never creates a stale finding.</p></div>}
      </section>
      {active && <IssueComparison issue={active} onClose={() => setSelected(null)} setReview={(next) => update(active, next)} />}
    </div>
    <details className="knowledge-diagnostics"><summary>Analysis diagnostics</summary><dl>{Object.entries(query.data.diagnostics).map(([label, value]) => <div key={label}><dt>{label.replace(/([A-Z])/g, " $1")}</dt><dd>{String(value)}</dd></div>)}</dl></details>
  </div>;
}
