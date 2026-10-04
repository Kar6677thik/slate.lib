"use client";
import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ArrowRight, ExternalLink, GitBranch, Link2, MessageCircleQuestion, Search, ShieldQuestion, X } from "lucide-react";
import { useApi } from "@/lib/auth/context";
import { useWorkspace } from "@/features/notes/workspace-context";
import { Button } from "@/components/ui/button";
import { ErrorMessage, Loading } from "@/components/common/primitives";
import { openAskSlate } from "@/components/ask/ask-slate";
import { linkSuggestionMarkdown, type LinkOpportunitySnapshot, type LinkSuggestion, type LinkSuggestionStatus, type LinkSuggestionType } from "@/lib/intelligence/smart-links";
import { useLinkReviews } from "@/lib/storage/link-reviews";

const labels: Record<LinkSuggestionType, string> = { mention: "Direct mention", related: "Strong connection", graph_bridge: "Graph relationship", project_relationship: "Project relationship", concept_reference: "Concept reference", better_target: "Better target", overview_relationship: "Overview relationship" };
function reviewed(snapshot: LinkOpportunitySnapshot, values: Record<string, { status: LinkSuggestionStatus }>) { return snapshot.suggestions.map((item) => ({ ...item, status: values[item.fingerprint]?.status ?? item.status })); }
function selectedSuggestion(item: LinkSuggestion, targetId: string): LinkSuggestion {
  const target = [item.target, ...item.alternateTargets].find((candidate) => candidate.noteId === targetId) ?? item.target;
  const targetChanged = target.noteId !== item.target.noteId;
  return { ...item, target, alternateTargets: [item.target, ...item.alternateTargets].filter((candidate) => candidate.noteId !== target.noteId), ambiguous: false, suggestedMarkdown: targetChanged && item.existingText ? linkSuggestionMarkdown(target, item.existingText) : item.suggestedMarkdown };
}

export function LinkOpportunityList({ snapshot, compact = false, filterOpen = true, onInsert }: { snapshot: LinkOpportunitySnapshot; compact?: boolean; filterOpen?: boolean; onInsert?: (suggestion: LinkSuggestion) => Promise<string | null> | string | null }) {
  const workspace = useWorkspace(); const reviews = useLinkReviews(workspace.scope); const [error, setError] = useState(""); const [chosen, setChosen] = useState<Record<string, string>>({});
  const items = reviewed(snapshot, reviews.reviews).filter((item) => !filterOpen || item.status === "open");
  if (!items.length) return <div className="link-empty"><Link2 size={22} /><strong>No link opportunities in this view</strong><p>Existing links and weak relationships are intentionally excluded.</p></div>;
  return <ul className={`link-opportunity-list ${compact ? "compact" : ""}`} aria-label="Link opportunities">{error && <li className="link-action-error" role="alert">{error}</li>}{items.map((raw) => {
    const item = selectedSuggestion(raw, chosen[raw.fingerprint] ?? raw.target.noteId); const conflict = item.hasKnowledgeIssue;
    return <li className="link-opportunity" key={item.fingerprint}>
      <header><span className="link-kind">{labels[item.suggestionType]}</span>{item.overlapKind === "partial-overlap" && <span>Partial overlap</span>}</header>
      <div className="link-route"><div><small>Source</small><strong>{item.sourceTitle}</strong><span>{item.sourcePath}</span></div><ArrowRight size={17} aria-hidden="true" /><div><small>Target</small><strong>{item.target.title}</strong><span>{item.target.heading ? `${item.target.path}#${item.target.heading}` : item.target.path}</span></div></div>
      {item.ambiguous && <label className="link-target-choice"><span>Choose target</span><select value={item.target.noteId} onChange={(event) => setChosen((value) => ({ ...value, [item.fingerprint]: event.target.value }))}>{[item.target, ...item.alternateTargets].map((target) => <option key={target.noteId} value={target.noteId}>{target.title} · {target.path}</option>)}</select></label>}
      {item.sourceExcerpt && <blockquote>{item.sourceExcerpt}</blockquote>}<p>{item.explanation}</p><ul>{item.signals.map((signal) => <li key={signal}>{signal}</li>)}</ul>
      {item.suggestedMarkdown && <div className="link-preview"><span>Preview</span><code>{item.suggestedMarkdown}</code></div>}
      {conflict && <p className="link-warning"><ShieldQuestion size={15} />Related, but these notes currently disagree. Review the issue before linking, or link anyway.</p>}
      <footer>
        {onInsert && item.suggestedMarkdown && <Button size="sm" onClick={async () => { setError(""); const reason = await onInsert(item); if (reason) setError(reason); else reviews.setReview(item.fingerprint, "inserted"); }}><Link2 size={15} />{item.suggestionType === "better_target" ? "Preview replacement" : conflict ? "Link anyway" : "Insert link"}</Button>}
        <Button variant="outline" size="sm" onClick={() => workspace.open(item.sourceNoteId)}>Open source</Button><Button variant="outline" size="sm" onClick={() => workspace.open(item.target.noteId)}><ExternalLink size={15} />Open target</Button>
        <Button variant="ghost" size="sm" onClick={() => openAskSlate({ scope: "selected", noteIds: [item.sourceNoteId, item.target.noteId], question: "How are these notes related, and would linking them improve navigation?", selectedText: item.explanation.slice(0, 4000) })}><MessageCircleQuestion size={15} />Ask</Button>
        {conflict && <Button variant="ghost" size="sm" onClick={() => workspace.openKnowledgeIssues({ kind: "note", noteId: item.sourceNoteId })}>Review issue</Button>}
        {(conflict || item.suggestionType === "better_target") && <Button variant="ghost" size="sm" onClick={() => workspace.openKnowledgeOverlap({ kind: "note", noteId: item.sourceNoteId })}>Compare</Button>}
        {item.suggestionType === "better_target" && <Button variant="ghost" size="sm" onClick={() => workspace.openEvolution({ kind: "selected", noteIds: [item.sourceNoteId, item.target.noteId] })}>View evolution</Button>}
        <Button variant="ghost" size="sm" onClick={() => reviews.setReview(item.fingerprint, "dismissed")}><X size={15} />Dismiss</Button><Button variant="ghost" size="sm" onClick={() => reviews.setReview(item.fingerprint, "not-relevant")}>Not relevant</Button>
      </footer>
    </li>;
  })}</ul>;
}

export function LinkOpportunitiesWorkspace({ scope }: { scope: LinkOpportunitySnapshot["scope"] }) {
  const api = useApi(); const workspace = useWorkspace(); const reviews = useLinkReviews(workspace.scope); const [search, setSearch] = useState(""); const [type, setType] = useState<LinkSuggestionType | "all">("all"); const [status, setStatus] = useState<LinkSuggestionStatus | "all">("open"); const [project, setProject] = useState("all"); const [source, setSource] = useState("all"); const [target, setTarget] = useState("all");
  const query = useQuery({ queryKey: ["link-opportunities", scope], queryFn: ({ signal }) => api.linkOpportunities(scope, signal), staleTime: 30_000 });
  const snapshot = useMemo(() => { if (!query.data) return null; const suggestions = reviewed(query.data, reviews.reviews).filter((item) => (type === "all" || item.suggestionType === type) && (status === "all" || item.status === status) && (project === "all" || (item.projectPath ?? item.sourcePath.split("/").slice(0, -1).join("/")) === project) && (source === "all" || item.sourceNoteId === source) && (target === "all" || item.target.noteId === target) && (!search || `${item.sourceTitle} ${item.sourcePath} ${item.target.title} ${item.target.path} ${item.sourceExcerpt} ${item.explanation}`.toLowerCase().includes(search.toLowerCase()))); return { ...query.data, suggestions }; }, [project, query.data, reviews.reviews, search, source, status, target, type]);
  if (query.isPending) return <Loading label="Finding useful missing relationships…" />; if (query.error) return <ErrorMessage error={query.error} retry={() => query.refetch()} />; if (!snapshot) return null;
  const all = reviewed(query.data, reviews.reviews); const d = query.data.diagnostics; const open = all.filter((item) => item.status === "open").length; const dismissed = all.filter((item) => item.status === "dismissed" || item.status === "not-relevant").length; const inserted = all.filter((item) => item.status === "inserted").length; const projects = [...new Set(all.map((item) => item.projectPath ?? item.sourcePath.split("/").slice(0, -1).join("/")).filter(Boolean))].sort(); const sources = [...new Map(all.map((item) => [item.sourceNoteId, item.sourceTitle])).entries()].sort((a, b) => a[1].localeCompare(b[1])); const targets = [...new Map(all.map((item) => [item.target.noteId, item.target.title])).entries()].sort((a, b) => a[1].localeCompare(b[1]));
  return <div className="link-workspace scroll-area"><header className="knowledge-hero"><div><span className="eyebrow">Relationship intelligence</span><h1>Link Opportunities</h1><p>Review a small set of missing relationships. Slate never inserts or replaces links without your approval.</p></div><Button variant="outline" onClick={() => query.refetch()}>Find opportunities</Button></header>
    <div className="link-diagnostics" aria-label="Link opportunity diagnostics"><span><strong>{open}</strong> open</span><span><strong>{dismissed}</strong> dismissed</span><span><strong>{inserted}</strong> inserted</span><span><strong>{d.directMentions}</strong> mentions</span><span><strong>{d.graphOpportunities}</strong> graph gaps</span><span><strong>{d.projectOpportunities}</strong> project</span><span><strong>{d.betterTargets}</strong> better targets</span>{d.bounded && <span>Bounds reached</span>}</div>
    {query.data.degraded && <p className="knowledge-degraded">{query.data.degraded}</p>}
    <div className="knowledge-controls"><label className="knowledge-search"><Search size={16} /><span className="sr-only">Search link opportunities</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search titles, paths, excerpts, explanations…" /></label><label><span>Type</span><select value={type} onChange={(event) => setType(event.target.value as typeof type)}><option value="all">All types</option>{Object.entries(labels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label><label><span>Project or folder</span><select value={project} onChange={(event) => setProject(event.target.value)}><option value="all">All projects</option>{projects.map((value) => <option key={value} value={value}>{value}</option>)}</select></label><label><span>Source note</span><select value={source} onChange={(event) => setSource(event.target.value)}><option value="all">All sources</option>{sources.map(([id, title]) => <option key={id} value={id}>{title}</option>)}</select></label><label><span>Target note</span><select value={target} onChange={(event) => setTarget(event.target.value)}><option value="all">All targets</option>{targets.map(([id, title]) => <option key={id} value={id}>{title}</option>)}</select></label><label><span>Review</span><select value={status} onChange={(event) => setStatus(event.target.value as typeof status)}><option value="open">Open</option><option value="inserted">Inserted</option><option value="dismissed">Dismissed</option><option value="not-relevant">Not relevant</option><option value="all">All states</option></select></label></div>
    <LinkOpportunityList snapshot={snapshot} filterOpen={false} />
  </div>;
}

export function ProjectLinkSummary({ path }: { path: string }) {
  const api = useApi(), workspace = useWorkspace(); const scope = { kind: "project" as const, path }; const query = useQuery({ queryKey: ["link-opportunities", scope], queryFn: ({ signal }) => api.linkOpportunities(scope, signal), staleTime: 60_000 });
  if (!query.data?.suggestions.length) return <p className="project-empty">No meaningful missing project relationships found.</p>;
  return <div className="project-knowledge-summary"><p>{query.data.suggestions.length} relationship opportunit{query.data.suggestions.length === 1 ? "y" : "ies"} may improve project navigation.</p>{query.data.suggestions.slice(0, 3).map((item) => <button key={item.fingerprint} onClick={() => workspace.openLinkOpportunities(scope)}><GitBranch size={15} /><span><strong>{item.sourceTitle} → {item.target.title}</strong><small>{labels[item.suggestionType]}</small></span><ArrowRight size={15} /></button>)}<Button variant="outline" size="sm" onClick={() => workspace.openLinkOpportunities(scope)}>Review relationships</Button></div>;
}

export function InboxLinkIndicator() {
  const api = useApi(), workspace = useWorkspace(); const query = useQuery({ queryKey: ["link-opportunities", { kind: "project", path: "inbox" }], queryFn: ({ signal }) => api.linkOpportunities({ kind: "project", path: "inbox" }, signal), staleTime: 60_000 });
  if (!query.data?.suggestions.length) return null; return <button className="destination-intelligence" onClick={() => workspace.openLinkOpportunities({ kind: "project", path: "inbox" })}><Link2 size={16} /><span><strong>{query.data.suggestions.length} likely target note{query.data.suggestions.length === 1 ? "" : "s"}</strong><small>Review links for Inbox captures</small></span><ArrowRight size={15} /></button>;
}
