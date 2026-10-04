"use client";

import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { ArrowRight, Clock3, FileClock, MessageCircleQuestion, RefreshCw, Search, Sparkles } from "lucide-react";
import { useApi } from "@/lib/auth/context";
import { useWorkspace } from "@/features/notes/workspace-context";
import { openAskSlate } from "@/components/ask/ask-slate";
import { Button } from "@/components/ui/button";
import { Empty, ErrorMessage, Loading, Modal } from "@/components/common/primitives";
import { MarkdownBody } from "@/components/reader/markdown";
import type { EvolutionEvent, EvolutionScope, EvolutionSource, EvolutionSynthesis } from "@/lib/intelligence/evolution";

function citationMarkdown(text: string, sources: EvolutionSource[]) {
  const valid = new Set(sources.map((source) => source.citationId));
  return text.replace(/\[(S\d+)\]/g, (match, id: string) => valid.has(id) ? `[${id}](#evolution-source-${id})` : match);
}

function displayDate(value: string | null) {
  return value ? new Date(value).toLocaleDateString(undefined, { dateStyle: "medium" }) : "Date unknown";
}

function scopeQuestion(scope: EvolutionScope) {
  if (scope.kind === "topic") return `How has ${scope.topic} evolved?`;
  if (scope.kind === "note") return "How did this note evolve, and what is current?";
  if (scope.kind === "selected") return "How did the thinking across these notes evolve?";
  return `How has the thinking in ${scope.path} evolved?`;
}

function Comparison({ event, onOpen }: { event: EvolutionEvent; onOpen: (source: EvolutionSource) => void }) {
  return <article className="evolution-comparison" aria-label="Before and after comparison">
    <header>
      <div><span className={`evolution-label ${event.confidence}`}>{event.label}</span><h2>{event.title}</h2></div>
      <time dateTime={event.timestamp ?? undefined}>{displayDate(event.timestamp)}</time>
    </header>
    <p className="evolution-summary">{event.summary}</p>
    <div className="evolution-diff">
      <section>
        <span>Before</span>
        {event.before ? <><p>{event.before.excerpt}</p><button type="button" onClick={() => onOpen(event.before!)}>Open historical source · {event.before.citationId}</button></> : <p className="muted">No earlier bounded snapshot was available.</p>}
      </section>
      <ArrowRight size={18} aria-hidden="true" />
      <section>
        <span>After</span>
        <p>{event.after.excerpt}</p>
        <button type="button" onClick={() => onOpen(event.after)}>{event.after.state === "current" ? "Open current note" : "Open historical source"} · {event.after.citationId}</button>
      </section>
    </div>
    <dl className="evolution-reason">
      <div><dt>Changed area</dt><dd>{event.changedHeadings.join(" · ") || "Document"}</dd></div>
      <div><dt>Why</dt><dd>{event.rationale ?? "Reason not documented."}</dd></div>
      <div><dt>Date authority</dt><dd>{event.dateSource === "git" ? "Git history" : event.dateSource === "metadata" ? "Explicit note metadata" : event.dateSource === "explicit-date" ? "Explicit date in note" : "No trusted date"}</dd></div>
    </dl>
  </article>;
}

function GeneratedEvolution({ value, onOpen }: { value: EvolutionSynthesis; onOpen: (source: EvolutionSource) => void }) {
  return <section className="evolution-synthesis" aria-label="Grounded evolution synthesis">
    <header><div><span>Grounded synthesis</span><h2>Evolution narrative</h2></div><small>{value.cached ? "Cached" : "Generated"}</small></header>
    <div onClick={(event) => {
      const link = (event.target as HTMLElement).closest<HTMLAnchorElement>('a[href^="#evolution-source-"]');
      if (!link) return;
      const citation = link.getAttribute("href")?.replace("#evolution-source-", "");
      const source = value.sources.find((item) => item.citationId === citation);
      if (source) { event.preventDefault(); onOpen(source); }
    }}><MarkdownBody source={citationMarkdown(value.markdown, value.sources)} /></div>
  </section>;
}

export function EvolutionWorkspace({ initialScope }: { initialScope: EvolutionScope }) {
  const api = useApi();
  const workspace = useWorkspace();
  const [scope, setScope] = useState(initialScope);
  const [topic, setTopic] = useState(initialScope.kind === "topic" ? initialScope.topic : initialScope.topic ?? "");
  const [selected, setSelected] = useState<string | null>(null);
  const [source, setSource] = useState<EvolutionSource | null>(null);
  const [filter, setFilter] = useState<"all" | "decision" | "implementation" | "question" | "possible">("all");
  useEffect(() => { setScope(initialScope); setTopic(initialScope.kind === "topic" ? initialScope.topic : initialScope.topic ?? ""); setSelected(null); }, [initialScope]);
  const ready = scope.kind !== "topic" || Boolean(scope.topic.trim());
  const snapshot = useQuery({ queryKey: ["evolution", scope], queryFn: ({ signal }) => api.evolution(scope, signal), enabled: ready, staleTime: 30_000, retry: false });
  const synthesis = useMutation({ mutationFn: (refresh: boolean) => api.evolutionSynthesis(scope, refresh) });
  useEffect(() => { if (snapshot.data?.events.length && !selected) setSelected(snapshot.data.events[0].id); }, [selected, snapshot.data]);
  const events = useMemo(() => (snapshot.data?.events ?? []).filter((event) => filter === "all" || filter === "decision" && /Decision/.test(event.type) || filter === "implementation" && event.type === "ArchitectureChanged" || filter === "question" && event.type === "QuestionAnswered" || filter === "possible" && event.type === "PossibleShift"), [filter, snapshot.data]);
  const active = snapshot.data?.events.find((event) => event.id === selected) ?? events[0];
  function analyze() {
    const value = topic.trim();
    if (!value) return;
    const next: EvolutionScope = { kind: "topic", topic: value };
    setScope(next); workspace.openEvolution(next); setSelected(null); synthesis.reset();
  }
  function openSource(value: EvolutionSource) {
    if (value.state === "current") workspace.open(value.noteId);
    else setSource(value);
  }
  return <div className="evolution-workspace scroll-area">
    <header className="evolution-hero">
      <div className="evolution-title"><FileClock size={24} /><div><span>Evolution of Thought</span><h1>{ready && snapshot.data ? snapshot.data.title : "Trace an idea through time"}</h1><p>Evidence from canonical notes, Git history, and explicit metadata.</p></div></div>
      <form className="evolution-topic" onSubmit={(event) => { event.preventDefault(); analyze(); }}>
        <Search size={17} /><input aria-label="Topic to trace" value={topic} onChange={(event) => setTopic(event.target.value)} placeholder="Topic, decision, architecture, or question" /><Button type="submit" size="sm" disabled={!topic.trim()}>Trace</Button>
      </form>
      {snapshot.data && <div className="evolution-actions">
        <Button variant="outline" onClick={() => openAskSlate({ scope: scope.kind === "note" ? "note" : scope.kind === "selected" ? "selected" : scope.kind === "project" ? "project" : scope.kind === "folder" ? "folder" : "library", path: "path" in scope ? scope.path : undefined, noteId: scope.kind === "note" ? scope.noteId : undefined, noteIds: scope.kind === "selected" ? scope.noteIds : undefined, question: scopeQuestion(scope) })}><MessageCircleQuestion size={15} />Ask about this evolution</Button>
        {snapshot.data.provider.available && <Button onClick={() => synthesis.mutate(false)} disabled={synthesis.isPending}><Sparkles size={15} />{synthesis.isPending ? "Synthesizing…" : "Synthesize"}</Button>}
        <Button variant="ghost" aria-label="Refresh evolution" onClick={() => { void snapshot.refetch(); synthesis.reset(); }}><RefreshCw size={15} /></Button>
      </div>}
    </header>

    {!ready ? <div className="evolution-start"><Clock3 size={30} /><h2>Choose a topic</h2><p>Slate will find relevant current notes, then inspect only a bounded set of their committed versions.</p></div>
    : snapshot.isPending ? <Loading label="Tracing meaningful changes…" />
    : snapshot.error ? <ErrorMessage error={snapshot.error} retry={() => snapshot.refetch()} />
    : snapshot.data && <>
      <div className="evolution-facts">
        <span>{snapshot.data.noteCount} notes</span><span>{snapshot.data.revisionCount} bounded revisions</span><span>{snapshot.data.events.length} meaningful changes</span>{snapshot.data.bounded && <strong>Bounded view</strong>}
      </div>
      {snapshot.data.degraded && <p className="evolution-degraded" role="status">{snapshot.data.degraded}</p>}
      {synthesis.error && <p className="evolution-degraded" role="status">Synthesis is temporarily unavailable. The evidence timeline remains complete.</p>}
      {synthesis.data && <GeneratedEvolution value={synthesis.data} onOpen={openSource} />}
      <div className="evolution-layout">
        <aside className="evolution-timeline" aria-label="Evolution timeline">
          <header><h2>Timeline</h2><div role="group" aria-label="Filter changes">{([ ["all", "All"], ["decision", "Decisions"], ["implementation", "Implementation"], ["question", "Questions"], ["possible", "Possible"] ] as const).map(([value, label]) => <button key={value} className={filter === value ? "active" : ""} onClick={() => setFilter(value)}>{label}</button>)}</div></header>
          {events.map((event) => <button type="button" key={event.id} className={active?.id === event.id ? "active" : ""} onClick={() => setSelected(event.id)}>
            <time>{displayDate(event.timestamp)}</time><span className={`evolution-dot ${event.confidence}`} /><span><strong>{event.title}</strong><small>{event.noteTitle} · {event.label}</small></span>
          </button>)}
          {!events.length && <Empty title="No matching changes" detail="Try another filter or a broader topic." />}
        </aside>
        <section className="evolution-detail">{active ? <Comparison event={active} onOpen={openSource} /> : <Empty title="No meaningful change found" detail="The bounded history contained only equivalent or cosmetic edits." />}</section>
        <aside className="evolution-current" aria-label="Current view">
          <header><span>Current only</span><h2>Current view</h2><p>These cards use the newest canonical revision, never an older snapshot.</p></header>
          {snapshot.data.current.map((item) => <button key={item.noteId} onClick={() => workspace.open(item.noteId)}><strong>{item.title}</strong><small>{item.path}</small><p>{item.excerpt}</p>{(item.type || item.status) && <span>{[item.type, item.status].filter(Boolean).join(" · ")}</span>}</button>)}
        </aside>
      </div>
      <details className="evolution-sources"><summary>Evidence sources <span>{snapshot.data.sources.length}</span></summary><div>{snapshot.data.sources.map((item) => <button id={`evolution-source-${item.citationId}`} key={item.citationId} onClick={() => openSource(item)}><strong>{item.citationId} · {item.title}</strong><small>{item.state} · {item.commit ? item.commit.slice(0, 8) : "current"} · {displayDate(item.timestamp)}</small><p>{item.excerpt}</p></button>)}</div></details>
    </>}
    <Modal open={Boolean(source)} onClose={() => setSource(null)} title={source?.title ?? "Historical source"} description={source ? `${source.path} · ${source.commit?.slice(0, 12) ?? source.revision} · ${displayDate(source.timestamp)}` : undefined} wide>
      {source && <div className="historical-source"><p>{source.excerpt}</p><div className="dialog-actions"><Button variant="outline" onClick={() => setSource(null)}>Close</Button><Button onClick={() => { const id = source.noteId; setSource(null); workspace.open(id); }}>Open current note</Button></div></div>}
    </Modal>
  </div>;
}

