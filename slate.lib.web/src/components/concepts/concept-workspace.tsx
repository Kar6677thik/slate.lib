"use client";

import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ArrowUpRight, BookOpen, BrainCircuit, FolderOpen, GitBranch, History, Link2, Merge, MessageCircleQuestion, Search, ShieldQuestion, Split, Tags } from "lucide-react";
import { useApi } from "@/lib/auth/context";
import { useWorkspace } from "@/features/notes/workspace-context";
import { openAskSlate } from "@/components/ask/ask-slate";
import { Button } from "@/components/ui/button";
import { Empty, ErrorMessage, Loading } from "@/components/common/primitives";
import { useConceptReviews } from "@/lib/storage/concept-reviews";
import type { ConceptPageSnapshot, ConceptSnapshot, KnowledgeConcept } from "@/lib/intelligence/concepts";
import { ConceptHealthSummary } from "@/components/health/library-health";
import { ConceptCoverageSummary } from "@/components/gaps/knowledge-gaps";

function isPage(value: ConceptSnapshot | ConceptPageSnapshot): value is ConceptPageSnapshot { return "concept" in value; }

function ConceptButton({ concept, open }: { concept: KnowledgeConcept; open: (name: string) => void }) {
  return <button className="concept-list-item" onClick={() => open(concept.canonicalName)}><span><strong>{concept.canonicalName}</strong><small>{concept.kind} · {concept.sourceCount} source{concept.sourceCount === 1 ? "" : "s"}</small></span><span>{concept.importanceSignals.slice(0, 2).join(" · ")}</span><ArrowUpRight size={15} /></button>;
}

function ConceptExplorer({ data, open }: { data: ConceptSnapshot; open: (name: string) => void }) {
  const [query, setQuery] = useState(""), [view, setView] = useState<"connected" | "referenced" | "recent" | "questions" | "issues">("connected"), [project, setProject] = useState("");
  const projects = useMemo(() => [...new Set(data.concepts.flatMap((concept) => concept.projectPaths))].sort(), [data.concepts]);
  const concepts = useMemo(() => data.concepts
    .filter((concept) => `${concept.canonicalName} ${concept.aliases.join(" ")} ${concept.tags.join(" ")}`.toLowerCase().includes(query.toLowerCase()))
    .filter((concept) => !project || concept.projectPaths.includes(project))
    .filter((concept) => view === "questions" ? concept.openQuestionCount > 0 : view === "issues" ? concept.knowledgeIssueCount > 0 : true)
    .sort((a, b) => view === "recent" ? (b.lastSeen ?? "").localeCompare(a.lastSeen ?? "") : view === "questions" ? b.openQuestionCount - a.openQuestionCount : view === "issues" ? b.knowledgeIssueCount - a.knowledgeIssueCount : view === "connected" ? b.relationships.length - a.relationships.length : b.sourceCount - a.sourceCount), [data.concepts, project, query, view]);
  return <div className="concept-workspace scroll-area"><header className="concept-hero"><div><span className="eyebrow">From my library</span><h1>Concepts</h1><p>Living views assembled from your notes, claims, links, projects, and history.</p></div></header>
    <div className="concept-controls"><label><Search size={16} /><span className="sr-only">Search concepts</span><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Find Kubernetes, PostgreSQL, MQTT…" /></label><select aria-label="Concept view" value={view} onChange={(event) => setView(event.target.value as typeof view)}><option value="connected">Most connected</option><option value="referenced">Most referenced</option><option value="recent">Recently active</option><option value="questions">With open questions</option><option value="issues">With knowledge issues</option></select><select aria-label="Filter concepts by project" value={project} onChange={(event) => setProject(event.target.value)}><option value="">All projects</option>{projects.map((path) => <option key={path} value={path}>{path}</option>)}</select></div>
    <section className="concept-list" aria-label="Concepts">{concepts.map((concept) => <ConceptButton key={concept.id} concept={concept} open={open} />)}{!concepts.length && <Empty title="No matching concepts" detail="Try an explicit title, alias, technology, project, or protocol from your library." />}</section>
    {data.degraded && <p className="knowledge-degraded">{data.degraded}</p>}
    <details className="knowledge-diagnostics"><summary>Concept diagnostics</summary><dl>{Object.entries(data.diagnostics).map(([key, value]) => <div key={key}><dt>{key.replace(/([A-Z])/g, " $1")}</dt><dd>{String(value)}</dd></div>)}</dl></details>
  </div>;
}

function SourceList({ title, items, open }: { title: string; items: ConceptPageSnapshot["keyNotes"]; open: (id: string) => void }) {
  if (!items.length) return null;
  return <details className="concept-section" open><summary><h2>{title}</h2><span>{items.length}</span></summary><div>{items.map((item) => <button className="concept-source" key={item.noteId} onClick={() => open(item.noteId)}><span><strong>{item.title}</strong><small>{item.path} · {item.strength}</small></span><p>{item.reasons.join(" · ")}</p><ArrowUpRight size={15} /></button>)}</div></details>;
}

function ConceptPage({ data, openConcept, refresh }: { data: ConceptPageSnapshot; openConcept: (name: string) => void; refresh: () => void }) {
  const workspace = useWorkspace(), reviews = useConceptReviews(workspace.scope), concept = data.concept; const ids = concept.members.slice(0, 20).map((member) => member.noteId);
  const ask = (question = `What does my library say about ${concept.canonicalName}?`) => openAskSlate({ scope: "selected", noteIds: ids, question });
  const search = () => { workspace.setQuery(`"${concept.canonicalName}"`); workspace.navigate("search"); };
  const merge = () => { const from = window.prompt("Alias or concept to merge into this derived identity"); if (from?.trim()) { reviews.merge(from, concept.canonicalName); refresh(); } };
  const alias = () => { const value = window.prompt(`Add a derived alias for ${concept.canonicalName}`); if (value?.trim()) { reviews.addAlias(concept.canonicalName, value); refresh(); } };
  const separate = () => { const value = window.prompt(`Concept to keep separate from ${concept.canonicalName}`); if (value?.trim()) { reviews.keepSeparate(concept.canonicalName, value); refresh(); } };
  return <div className="concept-workspace scroll-area"><header className="concept-page-hero"><div><span className="eyebrow">From my library</span><h1>{concept.canonicalName}</h1><p>{concept.kind}</p><small>{concept.sourceCount} notes · {concept.projectPaths.length} projects · {data.questions.length} questions · {data.issues.length} knowledge issues</small></div><div className="concept-actions"><Button onClick={() => ask()}><MessageCircleQuestion size={16} />Ask</Button><Button variant="outline" onClick={search}><Search size={16} />Search</Button><Button variant="outline" onClick={() => workspace.openEvolution({ kind: "topic", topic: concept.canonicalName })}><History size={16} />Evolution</Button></div></header>
    <nav className="project-jump" aria-label="Concept sections">{["overview", "coverage", "health", "notes", "projects", "related", "decisions", "questions", "timeline", "issues", "overlap", "links", "sources"].map((id) => <a key={id} href={`#concept-${id}`}>{id[0].toUpperCase() + id.slice(1)}</a>)}</nav>
    <div className="concept-content">
      <section className="concept-overview" id="concept-overview"><h2>Overview</h2><p>{concept.canonicalName} appears materially in {concept.sourceCount} notes{concept.projectPaths.length ? ` across ${concept.projectPaths.length} projects` : ""}. {concept.importanceSignals.join(". ")}.</p>{concept.aliases.length > 0 && <p><strong>Aliases:</strong> {concept.aliases.join(", ")}</p>}<div className="concept-review-actions"><Button variant="ghost" size="sm" onClick={merge}><Merge size={15} />Merge identity</Button><Button variant="ghost" size="sm" onClick={separate}><Split size={15} />Keep separate</Button><Button variant="ghost" size="sm" onClick={alias}><Tags size={15} />Add alias</Button></div></section>
      <section id="concept-coverage"><ConceptCoverageSummary conceptId={concept.id} conceptName={concept.canonicalName} /></section>
      <section id="concept-health"><ConceptHealthSummary conceptId={concept.id} conceptName={concept.canonicalName} /></section>
      <div id="concept-notes"><SourceList title="Key Notes" items={data.keyNotes} open={workspace.open} /></div>
      {data.projects.length > 0 && <details className="concept-section" id="concept-projects" open><summary><h2>Used In</h2><span>{data.projects.length}</span></summary><div>{data.projects.map((project) => <button className="concept-project" key={project.path} onClick={() => workspace.openProjectBrain(project.path)}><FolderOpen size={16} /><span><strong>{project.path}</strong><small>{project.sourceCount} source{project.sourceCount === 1 ? "" : "s"}{project.recentActivity ? ` · active ${new Date(project.recentActivity).toLocaleDateString()}` : ""}</small><p>{project.importantNote.title}</p></span><ArrowUpRight size={15} /></button>)}</div></details>}
      {concept.relationships.length > 0 && <details className="concept-section" id="concept-related" open><summary><h2>Related Concepts</h2><span>{concept.relationships.length}</span></summary><div className="concept-related-list">{concept.relationships.map((relationship) => <button key={`${relationship.targetConceptId}:${relationship.relationshipType}`} onClick={() => openConcept(relationship.targetName)}><span><strong>{relationship.targetName}</strong><small>{relationship.relationshipType.replaceAll("_", " ")}</small></span><p>{relationship.explanation}</p></button>)}</div><div className="concept-graph" aria-label="Accessible concept graph"><GitBranch size={16} /><p>Concept graph: {concept.canonicalName} with {data.graph.nodes.length - 1} bounded adjacent concepts. The relationship list above is the accessible graph alternative.</p></div></details>}
      {data.decisions.length > 0 && <details className="concept-section" id="concept-decisions" open><summary><h2>Decisions</h2><span>{data.decisions.length}</span></summary><div>{data.decisions.map((item, index) => <button className="concept-evidence" key={`${item.noteId}:${index}`} onClick={() => workspace.open(item.noteId)}><strong>{item.text}</strong><small>{item.title} · {item.path}</small></button>)}</div></details>}
      {data.questions.length > 0 && <details className="concept-section" id="concept-questions" open><summary><h2>Open Questions</h2><span>{data.questions.length}</span></summary><div>{data.questions.map((item, index) => <article className="concept-question" key={`${item.noteId}:${index}`}><button onClick={() => workspace.open(item.noteId)}><strong>{item.text}</strong><small>{item.title}</small></button><Button variant="ghost" size="sm" onClick={() => ask(`Help me answer this question about ${concept.canonicalName}: ${item.text}`)}>Ask Slate</Button></article>)}</div></details>}
      {data.timeline.length > 0 && <details className="concept-section" id="concept-timeline"><summary><h2>Timeline</h2><span>{data.timeline.length}</span></summary><ol className="concept-timeline">{data.timeline.map((item) => <li key={`${item.noteId}:${item.timestamp}`}><button onClick={() => workspace.open(item.noteId)}><time>{new Date(item.timestamp).toLocaleDateString()}</time><strong>{item.label}</strong><small>{item.title}</small></button></li>)}</ol></details>}
      {data.issues.length > 0 && <details className="concept-section" id="concept-issues" open><summary><h2>Knowledge Issues</h2><span>{data.issues.length}</span></summary><Button variant="outline" onClick={() => workspace.openKnowledgeIssues({ kind: "note", noteId: data.issues[0].sourceA.noteId })}><ShieldQuestion size={16} />Review concept issues</Button></details>}
      {data.overlaps.length > 0 && <details className="concept-section" id="concept-overlap"><summary><h2>Overlap</h2><span>{data.overlaps.length}</span></summary><Button variant="outline" onClick={() => workspace.openKnowledgeOverlap({ kind: "note", noteId: data.overlaps[0].noteA.noteId })}><BookOpen size={16} />Review overlapping notes</Button></details>}
      {data.linkOpportunities.length > 0 && <details className="concept-section" id="concept-links"><summary><h2>Link Opportunities</h2><span>{data.linkOpportunities.length}</span></summary><Button variant="outline" onClick={() => workspace.openLinkOpportunities({ kind: "note", noteId: data.linkOpportunities[0].sourceNoteId })}><Link2 size={16} />Review missing links</Button></details>}
      <SourceList title="Sources" items={concept.members} open={workspace.open} />
    </div>
    {data.degraded && <p className="knowledge-degraded">{data.degraded}</p>}
  </div>;
}

export function ConceptWorkspace({ identity }: { identity: string }) {
  const api = useApi(), workspace = useWorkspace(), reviews = useConceptReviews(workspace.scope);
  const query = useQuery({ queryKey: ["concepts", identity, reviews.reviews], queryFn: ({ signal }) => api.concepts({ identity: identity || undefined, reviews: reviews.reviews }, signal), staleTime: 30_000 });
  if (query.isPending) return <Loading label={identity ? `Building ${identity} from your library…` : "Indexing concepts…"} />;
  if (query.error || !query.data) return <ErrorMessage error={query.error ?? new Error("Concepts are unavailable")} retry={() => query.refetch()} />;
  return isPage(query.data) ? <ConceptPage data={query.data} openConcept={workspace.openConcept} refresh={() => void query.refetch()} /> : <ConceptExplorer data={query.data} open={workspace.openConcept} />;
}

export function CurrentNoteConcepts({ noteId }: { noteId: string }) {
  const api = useApi(), workspace = useWorkspace();
  const query = useQuery({ queryKey: ["concepts", "note", noteId], queryFn: ({ signal }) => api.concepts({}, signal) as Promise<ConceptSnapshot>, staleTime: 60_000 });
  const concepts = (query.data?.concepts ?? []).filter((concept) => concept.members.some((member) => member.noteId === noteId)).sort((a, b) => (b.members.find((member) => member.noteId === noteId)?.score ?? 0) - (a.members.find((member) => member.noteId === noteId)?.score ?? 0)).slice(0, 8);
  if (!concepts.length) return null;
  return <section className="rail-concepts"><h3>Concepts <span>{concepts.length}</span></h3>{concepts.map((concept) => <button key={concept.id} onClick={() => workspace.openConcept(concept.canonicalName)}><strong>{concept.canonicalName}</strong><small>{concept.kind}</small></button>)}</section>;
}

export function ProjectConceptSummary({ path }: { path: string }) {
  const api = useApi(), workspace = useWorkspace(); const query = useQuery({ queryKey: ["concepts", "project", path], queryFn: ({ signal }) => api.concepts({}, signal) as Promise<ConceptSnapshot>, staleTime: 60_000 });
  const concepts = (query.data?.concepts ?? []).filter((concept) => concept.projectPaths.includes(path) || concept.members.some((member) => member.path.startsWith(`${path}/`))).slice(0, 8);
  if (!concepts.length) return null;
  return <div className="project-concepts">{concepts.map((concept) => <button key={concept.id} onClick={() => workspace.openConcept(concept.canonicalName)}><BrainCircuit size={15} /><span><strong>{concept.canonicalName}</strong><small>{concept.sourceCount} sources · {concept.kind}</small></span><ArrowUpRight size={15} /></button>)}</div>;
}
