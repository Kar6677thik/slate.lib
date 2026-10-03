"use client";

import { useEffect, useRef, useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { formatDistanceToNow, isAfter, subDays } from "date-fns";
import { AlertTriangle, BrainCircuit, Clock3, FileQuestion, GitBranch, MessageCircleQuestion, RefreshCw, RotateCcw, Search } from "lucide-react";
import { useApi } from "@/lib/auth/context";
import { useWorkspace } from "@/features/notes/workspace-context";
import { useCommandRuntime } from "@/features/commands/runtime";
import { openAskSlate } from "@/components/ask/ask-slate";
import { Button } from "@/components/ui/button";
import { ErrorMessage, Loading } from "@/components/common/primitives";
import { MarkdownBody } from "@/components/reader/markdown";
import { headingSlug } from "@/lib/markdown/slate-markdown";
import type { AskSource, ProjectEvidence, ProjectSynthesis, ProjectTimelineEvent } from "@/lib/api/contracts";

function citationMarkdown(text: string, sources: AskSource[]) {
  const valid = new Set(sources.map((source) => source.citationId));
  return text.replace(/\[S(\d+)\]/g, (match, number: string) => valid.has(`S${Number(number)}`) ? `[S${Number(number)}](#project-source-S${Number(number)})` : match);
}

function SourceButton({ item, onOpen }: { item: ProjectEvidence; onOpen: (id: string, heading?: string | null) => void }) {
  return <button className="project-evidence" type="button" onClick={() => onOpen(item.noteId, item.heading)}>
    <span><strong>{item.title}</strong>{item.inferred && <em>Possible</em>}</span>
    <small>{item.path}{item.heading ? ` · ${item.heading}` : ""}</small>
    <p>{item.excerpt}</p>
  </button>;
}

function EvidenceSection({ id, title, items, onOpen, children }: { id: string; title: string; items: ProjectEvidence[]; onOpen: (id: string, heading?: string | null) => void; children?: React.ReactNode }) {
  if (!items.length && !children) return null;
  return <details className="project-section" id={id} open>
    <summary><h2>{title}</h2><span>{items.length || ""}</span></summary>
    <div className="project-section-body">{children}{items.map((item) => <SourceButton key={`${item.noteId}:${item.heading ?? ""}`} item={item} onOpen={onOpen} />)}</div>
  </details>;
}

function TimelineList({ items, onOpen, limit }: { items: ProjectTimelineEvent[]; onOpen: (id: string) => void; limit?: number }) {
  const visible = items.slice(0, limit);
  return <ol className="project-timeline">{visible.flatMap((item, index) => {
    const date = new Date(item.timestamp).toLocaleDateString(undefined, { dateStyle: "medium" });
    const previousDate = index ? new Date(visible[index - 1].timestamp).toLocaleDateString(undefined, { dateStyle: "medium" }) : null;
    const heading = date !== previousDate ? <li className="project-timeline-date" key={`date:${date}`}>{date}</li> : null;
    return [heading, <li key={item.id}><button onClick={() => onOpen(item.noteId)}><Clock3 size={14} /><span><strong>{item.label}</strong><small>{item.title} · {new Date(item.timestamp).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}</small></span></button></li>].filter(Boolean) as React.ReactNode[];
  })}</ol>;
}

function GeneratedSection({ result, title, onOpen }: { result?: ProjectSynthesis; title: string; onOpen: (id: string, heading?: string | null) => void }) {
  if (!result) return null;
  return <section className="project-generated" aria-label={title}>
    <header><div><span>Grounded synthesis</span><h2>{title}</h2></div><small>{result.cached ? "Cached" : "Generated"} {formatDistanceToNow(new Date(result.generatedAt), { addSuffix: true })}</small></header>
    <div onClick={(event) => {
      const link = (event.target as HTMLElement).closest<HTMLAnchorElement>('a[href^="#project-source-"]');
      if (!link) return;
      const id = link.getAttribute("href")?.replace("#project-source-", "");
      const source = result.sources.find((item) => item.citationId === id);
      if (source) { event.preventDefault(); onOpen(source.noteId, source.heading); }
    }}><MarkdownBody source={citationMarkdown(result.markdown, result.sources)} /></div>
    <details className="project-generated-sources">
      <summary>Sources <span>{result.sources.length}</span></summary>
      <div>{result.sources.map((source) => <button key={source.citationId} type="button" onClick={() => onOpen(source.noteId, source.heading)}>
        <strong>{source.citationId} · {source.title}</strong><small>{source.path}{source.heading ? ` · ${source.heading}` : ""}</small><span>{source.excerpt}</span>
      </button>)}</div>
    </details>
  </section>;
}

export function ProjectBrain({ path, initialSection }: { path: string; initialSection?: string | null }) {
  const api = useApi();
  const workspace = useWorkspace();
  const commands = useCommandRuntime();
  const [windowDays, setWindowDays] = useState<7 | 30 | 90 | 0>(30);
  const [graphDepth, setGraphDepth] = useState<1 | 2 | 0>(2);
  const [graphType, setGraphType] = useState("all");
  const [graphRelations, setGraphRelations] = useState<"explicit" | "hybrid">("explicit");
  const initialActionHandled = useRef(false);
  const snapshot = useQuery({ queryKey: ["project-brain", path], queryFn: ({ signal }) => api.projectBrain(path, signal), staleTime: 30_000 });
  const overview = useQuery({
    queryKey: ["project-synthesis", path, snapshot.data?.sourceFingerprint, "overview"],
    queryFn: ({ signal }) => api.projectSynthesis(path, "overview", false, signal),
    enabled: Boolean(snapshot.data?.provider.available),
    retry: false,
  });
  const resume = useMutation<ProjectSynthesis, Error, boolean>({ mutationFn: (refresh) => api.projectSynthesis(path, "resume", refresh) });
  const recentSummary = useMutation({ mutationFn: () => api.projectSynthesis(path, "recent") });
  useEffect(() => {
    if (initialSection !== "resume" || initialActionHandled.current || !snapshot.data) return;
    initialActionHandled.current = true;
    resume.mutate(false);
  }, [initialSection, resume, snapshot.data]);
  useEffect(() => {
    if (!initialSection || snapshot.isPending) return;
    requestAnimationFrame(() => document.getElementById(`project-${initialSection}`)?.scrollIntoView({ block: "start" }));
  }, [initialSection, snapshot.isPending]);

  function openSource(id: string, heading?: string | null) {
    workspace.open(id);
    if (heading) setTimeout(() => document.getElementById(headingSlug(heading))?.scrollIntoView({ block: "start" }), 500);
  }
  function ask(question?: string) { openAskSlate({ scope: "project", path, question }); }
  function answerQuestion(item: ProjectEvidence) {
    openSource(item.noteId, item.heading);
    window.setTimeout(() => { void commands.run("document.answer-question"); }, 450);
  }
  function searchProject() {
    workspace.setQuery(`path:${JSON.stringify(path)} `);
    workspace.navigate("search");
  }

  if (snapshot.isPending) return <div className="project-loading"><Loading label="Building the project view…" /></div>;
  if (snapshot.error) return <ErrorMessage error={snapshot.error} retry={() => snapshot.refetch()} />;
  const data = snapshot.data!;
  const recent = data.timeline.filter((item) => !windowDays || isAfter(new Date(item.timestamp), subDays(new Date(), windowDays)));
  const graphAdjacency = new Map<string, string[]>();
  for (const edge of data.graph.edges) {
    graphAdjacency.set(edge.source, [...(graphAdjacency.get(edge.source) ?? []), edge.target]);
    graphAdjacency.set(edge.target, [...(graphAdjacency.get(edge.target) ?? []), edge.source]);
  }
  const graphVisible = new Set<string>();
  const graphQueue: Array<{ id: string; depth: number }> = data.graph.nodes[0] ? [{ id: data.graph.nodes[0].id, depth: 0 }] : [];
  while (graphQueue.length) {
    const current = graphQueue.shift()!;
    if (graphVisible.has(current.id)) continue;
    graphVisible.add(current.id);
    if (graphDepth && current.depth >= graphDepth) continue;
    for (const id of graphAdjacency.get(current.id) ?? []) graphQueue.push({ id, depth: current.depth + 1 });
  }
  const graphNodes = data.graph.nodes.filter((node) => (!graphDepth || graphVisible.has(node.id)) && (graphType === "all" || node.kind === graphType));
  return <div className="project-brain scroll-area">
    <header className="project-hero">
      <div className="project-identity"><BrainCircuit size={25} /><div><span>Project Brain</span><h1>{data.name}</h1><p>{data.path}</p></div></div>
      <div className="project-actions">
        <Button onClick={() => resume.mutate(false)} disabled={resume.isPending || !data.provider.available}><RotateCcw size={16} />{resume.isPending ? "Preparing context…" : "Resume Project"}</Button>
        <Button variant="outline" onClick={() => ask()}><MessageCircleQuestion size={16} />Ask this project</Button>
        <Button variant="ghost" onClick={searchProject}><Search size={16} />Search</Button>
        <Button variant="ghost" aria-label="Refresh Project Brain" onClick={() => { void snapshot.refetch(); void overview.refetch(); }}><RefreshCw size={16} /></Button>
      </div>
      <dl className="project-stats">
        <div><dt>Status</dt><dd>{data.status}</dd></div>
        <div><dt>Notes</dt><dd>{data.noteCount}{data.bounded ? "+" : ""}</dd></div>
        <div><dt>Open questions</dt><dd>{data.openQuestionCount}</dd></div>
        <div><dt>Last meaningful change</dt><dd>{data.lastMeaningfulChange ? formatDistanceToNow(new Date(data.lastMeaningfulChange), { addSuffix: true }) : "No dated history"}</dd></div>
      </dl>
      {data.bounded && <p className="project-bound"><AlertTriangle size={14} />Large project: the view uses bounded candidates and can be refined with project search.</p>}
    </header>

    <nav className="project-jump" aria-label="Project Brain sections">
      {["overview", "current", "recent", "decisions", "questions", "architecture", "ideas", "experiments", "failures", "risks", "timeline", "graph", "sources"].map((id) => <a key={id} href={`#project-${id}`}>{id.replace(/^./, (v) => v.toUpperCase())}</a>)}
    </nav>

    <div className="project-content">
      {data.provider.available && overview.isPending && <div className="project-generating" role="status">Building a grounded overview while deterministic sections remain available…</div>}
      {!data.provider.available && <div className="project-ai-off" role="status">AI synthesis isn’t configured. Project evidence, history, search, and source navigation remain available.</div>}
      {data.provider.available && overview.error && <div className="project-ai-off" role="status">AI synthesis is temporarily unavailable. Deterministic project evidence remains available.</div>}
      <GeneratedSection result={overview.data} title="Overview" onOpen={openSource} />
      <EvidenceSection id="project-overview" title="Overview sources" items={data.sections.overview} onOpen={openSource} />
      <EvidenceSection id="project-current" title="Current State" items={data.sections.current} onOpen={openSource} />

      <details className="project-section" id="project-recent" open><summary><h2>Recent Changes</h2><span>{recent.length}</span></summary><div className="project-section-body">
        <div className="project-time-filters" aria-label="Recent changes time window">{([7, 30, 90, 0] as const).map((days) => <button key={days} className={windowDays === days ? "active" : ""} onClick={() => setWindowDays(days)}>{days ? `${days} days` : "All"}</button>)}{data.provider.available && <button onClick={() => recentSummary.mutate()} disabled={recentSummary.isPending}>Summarize changes</button>}</div>
        <GeneratedSection result={recentSummary.data} title="What changed recently" onOpen={openSource} />
        <TimelineList items={recent} onOpen={openSource} limit={30} />
        {!recent.length && <p className="project-empty">No dated changes in this window.</p>}
      </div></details>

      <EvidenceSection id="project-decisions" title="Decisions" items={data.sections.decisions} onOpen={openSource} />
      {data.sections.questions.length > 0 && <details className="project-section" id="project-questions" open><summary><h2>Open Questions</h2><span>{data.sections.questions.length}</span></summary><div className="project-section-body">
        <div className="project-inline-actions"><Button variant="outline" size="sm" onClick={() => ask("What remains unresolved in this project?")}><FileQuestion size={14} />Ask about unresolved questions</Button></div>
        {data.sections.questions.map((item) => { const linked = data.graph.edges.filter((edge) => edge.source === item.noteId || edge.target === item.noteId).length; return <article className="project-question" key={`${item.noteId}:${item.heading ?? ""}`}><div className="project-question-content"><SourceButton item={item} onOpen={openSource} /><p>{item.status ?? "open"}{item.timestamp ? ` · changed ${formatDistanceToNow(new Date(item.timestamp), { addSuffix: true })}` : ""}{linked ? ` · ${linked} linked project note${linked === 1 ? "" : "s"}` : ""}</p></div><div className="project-question-actions"><Button variant="ghost" size="sm" onClick={() => openSource(item.noteId, item.heading)}>Open</Button><Button variant="ghost" size="sm" onClick={() => answerQuestion(item)}>Answer</Button><Button variant="ghost" size="sm" onClick={() => ask(`Help me answer this project question: ${item.excerpt}`)}>Ask</Button></div></article>; })}
      </div></details>}
      <EvidenceSection id="project-architecture" title="Architecture / Structure" items={data.sections.architecture} onOpen={openSource} />
      <EvidenceSection id="project-ideas" title="Ideas" items={data.sections.ideas} onOpen={openSource} />
      <EvidenceSection id="project-experiments" title="Experiments" items={data.sections.experiments} onOpen={openSource} />
      <EvidenceSection id="project-failures" title="Failures" items={data.sections.failures} onOpen={openSource} />
      <EvidenceSection id="project-risks" title="Risks / Blockers" items={data.sections.risks} onOpen={openSource} />

      <details className="project-section" id="project-timeline"><summary><h2>Timeline</h2><span>{data.timeline.length}</span></summary><div className="project-section-body"><TimelineList items={data.timeline} onOpen={openSource} /></div></details>
      {data.graph.nodes.length > 0 && <details className="project-section" id="project-graph"><summary><h2>Project Graph</h2><span>{graphNodes.length}</span></summary><div className="project-section-body"><p className="project-section-intro"><GitBranch size={14} />Bounded project relationships. The accessible list is the primary fallback and remains available alongside relationship data.</p>
        <div className="project-graph-controls"><label>Depth<select value={graphDepth} onChange={(event) => setGraphDepth(Number(event.target.value) as 0 | 1 | 2)}><option value={1}>1</option><option value={2}>2</option><option value={0}>All bounded</option></select></label><label>Note type<select value={graphType} onChange={(event) => setGraphType(event.target.value)}><option value="all">All</option><option value="architecture">Architecture</option><option value="decision">Decisions</option><option value="question">Questions</option><option value="note">Notes</option></select></label><label>Relations<select value={graphRelations} onChange={(event) => setGraphRelations(event.target.value as "explicit" | "hybrid")}><option value="explicit">Explicit links</option><option value="hybrid">Include semantic related</option></select></label></div>
        <div className="project-graph-list">{graphNodes.map((node) => <button key={node.id} onClick={() => openSource(node.id)}><span>{node.title}</span><small>{node.kind} · {node.path}</small></button>)}{graphRelations === "hybrid" && data.relatedOutside.map((node) => <button key={`outside:${node.id}`} onClick={() => openSource(node.id)}><span>{node.title}</span><small>related outside project · {node.path}</small></button>)}</div>{!graphNodes.length && <p className="project-empty">No project nodes match these graph filters.</p>}</div></details>}
      {data.relatedOutside.length > 0 && <details className="project-section"><summary><h2>Related from Elsewhere</h2><span>{data.relatedOutside.length}</span></summary><div className="project-section-body"><p className="project-section-intro">These notes are outside {data.path} and are excluded from project synthesis.</p>{data.relatedOutside.map((item) => <button className="project-related" key={item.id} onClick={() => openSource(item.id)}><strong>{item.title}</strong><small>{item.path}</small><span>{item.reasons.join(" · ")}</span></button>)}</div></details>}
      <EvidenceSection id="project-sources" title="Important Sources" items={data.sections.important} onOpen={openSource} />
    </div>

    {(resume.data || resume.error) && <aside className="project-resume" aria-label="Resume Project context pack">
      <header><div><span>Continue with context</span><h2>Resume Project</h2></div><Button variant="ghost" size="sm" onClick={() => resume.reset()}>Close</Button></header>
      {resume.error ? <ErrorMessage error={resume.error} retry={() => resume.mutate(false)} /> : <>
        <GeneratedSection result={resume.data} title="Context pack" onOpen={openSource} />
        <footer><span>Generated {resume.data ? formatDistanceToNow(new Date(resume.data.generatedAt), { addSuffix: true }) : ""}</span><Button variant="outline" size="sm" onClick={() => resume.mutate(true)} disabled={resume.isPending}>Refresh synthesis</Button></footer>
      </>}
    </aside>}
  </div>;
}

