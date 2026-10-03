"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Copy, ExternalLink, FileText, MessageCircleQuestion, RotateCcw, Square, Trash2 } from "lucide-react";
import { useApi } from "@/lib/auth/context";
import { useWorkspace } from "@/features/notes/workspace-context";
import { Modal } from "@/components/common/primitives";
import { Button } from "@/components/ui/button";
import { MarkdownBody } from "@/components/reader/markdown";
import { headingSlug } from "@/lib/markdown/slate-markdown";
import type { AskPolicy, AskScope, AskSource, AskTurn } from "@/lib/api/contracts";

interface ConversationMessage {
  id: string;
  role: "user" | "assistant";
  text: string;
  sources?: AskSource[];
  citations?: string[];
  error?: string;
  stopped?: boolean;
}

interface AskOpenDetail {
  scope?: AskScope["kind"];
  selectedText?: string;
  path?: string;
  question?: string;
}

const eventName = "slate:ask";
export function openAskSlate(detail: AskOpenDetail = {}) {
  window.dispatchEvent(new CustomEvent<AskOpenDetail>(eventName, { detail }));
}

function storageKey(scope: string) { return `slate.ask.${scope}`; }
function loadConversation(scope: string): ConversationMessage[] {
  try {
    const value = JSON.parse(sessionStorage.getItem(storageKey(scope)) ?? "[]");
    if (!Array.isArray(value)) return [];
    return value.filter((item) => item && (item.role === "user" || item.role === "assistant") && typeof item.text === "string").slice(-12).map((item) => ({ ...item, text: item.text.slice(0, 48_000) }));
  } catch { return []; }
}
function citationMarkdown(text: string, sources: AskSource[]) {
  const valid = new Set(sources.map((source) => source.citationId));
  return text.replace(/\[S(\d+)\]/g, (match, number: string) => {
    const id = `S${Number(number)}`;
    return valid.has(id) ? `[${id}](#ask-source-${id})` : match;
  });
}
function parentFolder(path: string) { return path.split("/").slice(0, -1).join("/"); }
function projectFolder(path: string) { return path.split("/").slice(0, -1)[0] ?? ""; }

export function AskSlate() {
  const api = useApi();
  const workspace = useWorkspace();
  const active = workspace.tabs.find((tab) => tab.id === workspace.active);
  const [open, setOpen] = useState(false);
  const [scopeKind, setScopeKind] = useState<AskScope["kind"]>("library");
  const [policy, setPolicy] = useState<AskPolicy>("strict");
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [selectedText, setSelectedText] = useState("");
  const [requestedPath, setRequestedPath] = useState("");
  const [question, setQuestion] = useState("");
  const [messages, setMessages] = useState<ConversationMessage[]>([]);
  const [running, setRunning] = useState(false);
  const [retrieval, setRetrieval] = useState("");
  const abort = useRef<AbortController | null>(null);
  const lastQuestion = useRef("");
  const status = useQuery({ queryKey: ["intelligence-status"], queryFn: ({ signal }) => api.intelligenceStatus(signal), retry: false, enabled: open });

  useEffect(() => setMessages(loadConversation(workspace.scope)), [workspace.scope]);
  useEffect(() => {
    if (typeof sessionStorage !== "undefined") sessionStorage.setItem(storageKey(workspace.scope), JSON.stringify(messages.slice(-12)));
  }, [messages, workspace.scope]);
  useEffect(() => {
    const handler = (event: Event) => {
      const detail = (event as CustomEvent<AskOpenDetail>).detail ?? {};
      const requested = detail.scope ?? (active ? "note" : "library");
      setScopeKind(requested === "note" && !active ? "library" : requested);
      setSelectedText(detail.selectedText?.slice(0, 4_000) ?? "");
      setRequestedPath(detail.path?.replace(/\\/g, "/").replace(/^\/+|\/+$/g, "") ?? "");
      if (detail.question) setQuestion(detail.question.slice(0, 2_000));
      if (requested === "selected") setSelectedIds(workspace.tabs.map((tab) => tab.id).slice(0, 20));
      else if (active) setSelectedIds([active.id]);
      setOpen(true);
    };
    window.addEventListener(eventName, handler);
    return () => window.removeEventListener(eventName, handler);
  }, [active, workspace.tabs]);

  const folder = active ? parentFolder(active.path) : "";
  const project = requestedPath || workspace.projectPath || (active ? projectFolder(active.path) : "");
  const resolvedScope = useMemo<AskScope>(() => {
    if (scopeKind === "note" && active) return { kind: "note", noteId: active.id, selectedText: selectedText || undefined };
    if (scopeKind === "folder" && folder) return { kind: "folder", path: folder, selectedText: selectedText || undefined };
    if (scopeKind === "project" && project) return { kind: "project", path: project, selectedText: selectedText || undefined };
    if (scopeKind === "selected" && selectedIds.length) return { kind: "selected", noteIds: selectedIds, selectedText: selectedText || undefined };
    return { kind: "library", selectedText: selectedText || undefined };
  }, [active, folder, project, scopeKind, selectedIds, selectedText]);

  const scopeLabel = resolvedScope.kind === "note" ? active?.title ?? "Current note"
    : resolvedScope.kind === "folder" ? `Folder: ${resolvedScope.path}`
      : resolvedScope.kind === "project" ? `Project: ${resolvedScope.path}`
        : resolvedScope.kind === "selected" ? `${resolvedScope.noteIds?.length ?? 0} selected notes`
          : "Entire library";

  async function ask(value = question) {
    const clean = value.trim();
    if (!clean || running || !status.data?.askEnabled) return;
    lastQuestion.current = clean;
    setQuestion("");
    setRetrieval("Starting retrieval…");
    const user: ConversationMessage = { id: crypto.randomUUID(), role: "user", text: clean };
    const assistantId = crypto.randomUUID();
    const assistant: ConversationMessage = { id: assistantId, role: "assistant", text: "", sources: [], citations: [] };
    const history = messages.slice(-6).map<AskTurn>((message) => ({ role: message.role, text: message.text.slice(0, 4_000) }));
    setMessages((current) => [...current, user, assistant].slice(-12));
    const controller = new AbortController();
    abort.current = controller;
    setRunning(true);
    try {
      await api.askSlate({ question: clean, scope: resolvedScope, policy, conversation: history }, (event) => {
        if (event.type === "retrieval") setRetrieval(event.message);
        if (event.type === "sources") {
          setRetrieval(`${event.sources.length} source excerpt${event.sources.length === 1 ? "" : "s"} found`);
          setMessages((current) => current.map((message) => message.id === assistantId ? { ...message, sources: event.sources } : message));
        }
        if (event.type === "delta") setMessages((current) => current.map((message) => message.id === assistantId ? { ...message, text: message.text + event.text } : message));
        if (event.type === "replace") setMessages((current) => current.map((message) => message.id === assistantId ? { ...message, text: event.text } : message));
        if (event.type === "done") {
          setMessages((current) => current.map((message) => message.id === assistantId ? { ...message, citations: event.citations } : message));
          setRetrieval("");
        }
        if (event.type === "error") setMessages((current) => current.map((message) => message.id === assistantId ? { ...message, error: event.message } : message));
      }, controller.signal);
    } catch (error) {
      if (controller.signal.aborted) setMessages((current) => current.map((message) => message.id === assistantId ? { ...message, stopped: true } : message));
      else setMessages((current) => current.map((message) => message.id === assistantId ? { ...message, error: error instanceof Error ? error.message : "Ask Slate could not answer." } : message));
    } finally {
      abort.current = null;
      setRunning(false);
      setRetrieval("");
    }
  }

  function openSource(source: AskSource, close: boolean) {
    workspace.open(source.noteId);
    if (close) setOpen(false);
    if (source.heading) setTimeout(() => document.getElementById(headingSlug(source.heading!))?.scrollIntoView({ block: "start" }), 500);
  }

  const suggestions = active ? ["Summarize this note", "Find related knowledge", "What questions remain?", "What changed historically?"] : ["What decisions are recorded in my library?", "What questions are still open?"];
  return (
    <Modal open={open} onClose={() => { if (!running) setOpen(false); }} title="Ask Slate" description="Answers grounded in your own sources." wide>
      <div className="ask-slate">
        <div className="ask-controls">
          <label>Scope<select aria-label="Ask scope" value={scopeKind} onChange={(event) => setScopeKind(event.target.value as AskScope["kind"])}>
            <option value="library">Entire library</option>
            <option value="note" disabled={!active}>Current note</option>
            <option value="folder" disabled={!folder}>Current folder</option>
            <option value="project" disabled={!project}>Current project</option>
            <option value="selected" disabled={!workspace.tabs.length}>Selected notes</option>
          </select></label>
          <label>Answer policy<select aria-label="Answer policy" value={policy} onChange={(event) => setPolicy(event.target.value as AskPolicy)}>
            <option value="strict">Strict library</option><option value="general">General + library</option>
          </select></label>
          <span className="ask-scope-label">{scopeLabel}</span>
        </div>
        {scopeKind === "selected" && <fieldset className="ask-note-selection"><legend>Notes to include</legend>{workspace.tabs.map((tab) => <label key={tab.id}><input type="checkbox" checked={selectedIds.includes(tab.id)} onChange={() => setSelectedIds((current) => current.includes(tab.id) ? current.filter((id) => id !== tab.id) : [...current, tab.id].slice(0, 20))} />{tab.title}</label>)}</fieldset>}
        {!status.isPending && !status.data?.askEnabled && <div className="ask-unavailable" role="status"><MessageCircleQuestion size={20} /><div><strong>Ask Slate isn’t configured on this server.</strong><p>Search and the rest of Slate continue to work normally.</p></div></div>}
        <div className="ask-conversation" aria-live="polite">
          {!messages.length && status.data?.askEnabled && <div className="ask-start"><strong>Research your Slate library</strong><p>Every library claim links back to a source excerpt.</p><div>{suggestions.map((suggestion) => <button type="button" key={suggestion} onClick={() => setQuestion(suggestion)}>{suggestion}</button>)}</div></div>}
          {messages.map((message) => <section key={message.id} className={`ask-turn ${message.role}`} aria-label={message.role === "user" ? "Your question" : "Ask Slate answer"}>
            <header><span>{message.role === "user" ? "Question" : "Answer"}</span>{message.role === "assistant" && message.text && <Button variant="ghost" size="sm" onClick={() => void navigator.clipboard.writeText(message.text)}><Copy size={14} />Copy</Button>}</header>
            {message.role === "user" ? <p>{message.text}</p> : <>
              {message.text && <div onClick={(event) => {
                const link = (event.target as HTMLElement).closest<HTMLAnchorElement>('a[href^="#ask-source-"]');
                if (!link) return;
                const citationId = link.getAttribute("href")?.replace("#ask-source-", "");
                const source = message.sources?.find((item) => item.citationId === citationId);
                if (source) { event.preventDefault(); openSource(source, true); }
              }}><MarkdownBody source={citationMarkdown(message.text, message.sources ?? [])} /></div>}
              {!message.text && running && <p className="ask-working">Reviewing sources…</p>}
              {message.stopped && <p className="ask-note">Generation stopped. The partial answer was kept.</p>}
              {message.error && <div className="error-message" role="alert">{message.error}<Button variant="outline" size="sm" onClick={() => ask(lastQuestion.current)}>Retry</Button></div>}
              {!!message.sources?.length && <div className="ask-sources"><h3>Sources</h3>{message.sources.map((source) => <article id={`ask-source-${source.citationId}`} key={source.citationId} className={message.citations?.includes(source.citationId) ? "cited" : ""}>
                <button className="ask-source-main" type="button" onClick={() => openSource(source, true)}><span>{source.citationId}</span><div><strong>{source.title}</strong><small>{source.path}{source.heading ? ` · ${source.heading}` : ""}</small><p>{source.excerpt}</p></div></button>
                <div><Button variant="ghost" size="sm" onClick={() => openSource(source, true)}><FileText size={14} />Open note</Button><Button variant="ghost" size="sm" onClick={() => openSource(source, false)}><ExternalLink size={14} />Open in tab</Button><Button variant="ghost" size="sm" onClick={() => void navigator.clipboard.writeText(`[[${source.title}${source.heading ? `#${source.heading}` : ""}]]`)}><Copy size={14} />Copy link</Button></div>
              </article>)}</div>}
            </>}
          </section>)}
        </div>
        {retrieval && <div className="ask-retrieval" role="status">{retrieval}</div>}
        <form className="ask-composer" onSubmit={(event) => { event.preventDefault(); void ask(); }}>
          <label htmlFor="ask-question">Question</label><textarea id="ask-question" rows={3} maxLength={2_000} value={question} onChange={(event) => setQuestion(event.target.value)} placeholder="Ask a question about your Slate knowledge…" disabled={running || !status.data?.askEnabled} />
          <div><span>{policy === "strict" ? "Uses only Slate evidence" : "Separates library evidence from general context"}</span>{running ? <Button type="button" variant="outline" onClick={() => abort.current?.abort()}><Square size={14} />Stop</Button> : <Button type="submit" disabled={!question.trim() || !status.data?.askEnabled}>Ask Slate</Button>}</div>
        </form>
        <footer className="ask-footer"><Button variant="ghost" size="sm" onClick={() => { setMessages([]); setQuestion(""); }}><RotateCcw size={14} />New conversation</Button><Button variant="ghost" size="sm" onClick={() => { setMessages([]); sessionStorage.removeItem(storageKey(workspace.scope)); }}><Trash2 size={14} />Clear</Button></footer>
      </div>
    </Modal>
  );
}
