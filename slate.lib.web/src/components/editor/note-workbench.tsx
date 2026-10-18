"use client";
import {
  useEffect,
  useState,
  useRef,
  useCallback,
  useDeferredValue,
} from "react";
import dynamic from "next/dynamic";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Save,
  Pencil,
  Eye,
  Columns2,
  Bold,
  Italic,
  Heading2,
  Code,
  SquareCode,
  Quote,
  List,
  ListOrdered,
  ListTodo,
  ListChecks,
  GraduationCap,
  CircleSlash,
  Link,
  Braces,
  MessageCircleQuestion,
  FileOutput,
  History,
  ShieldQuestion,
  Layers3,
  Link2,
  Sparkles,
} from "lucide-react";
import type { EditorView } from "@codemirror/view";
import type { Note } from "@/lib/api/contracts";
import { useApi } from "@/lib/auth/context";
import { ApiError } from "@/lib/api/client";
import { useWorkspace } from "@/features/notes/workspace-context";
import { usePreferences } from "@/lib/storage/preferences";
import { recordRecent } from "@/lib/storage/recent";
import { drafts, type Draft } from "@/lib/storage/drafts";
import { frontmatterField, setFrontmatterField } from "@/lib/markdown/frontmatter";
import {
  IconButton,
  ErrorMessage,
  Loading,
  Modal,
} from "@/components/common/primitives";
import { Button } from "@/components/ui/button";
const Markdown = dynamic(() => import("@/components/reader/markdown"), {
  ssr: false,
});
import { UploadTools } from "@/components/assets/upload-tools";
import { assetMarkdown } from "@/lib/markdown/assets";
import { ItemMenu, type ItemMenuAction } from "@/components/library/item-menu";
import type { Format } from "./codemirror";
import { WikiExportDialog } from "@/components/links/wiki-export-dialog";
import { useCommandRuntime } from "@/features/commands/runtime";
import { openAskSlate } from "@/components/ask/ask-slate";
import { LinkOpportunityList } from "@/components/links/link-opportunities";
import { takeEditingRequest } from "@/features/notes/edit-intent";
import { toggleTaskAt } from "@/lib/markdown/tasks";
import {
  applyLinkSuggestionDraft,
  type LinkSuggestion,
} from "@/lib/intelligence/smart-links";
const Editor = dynamic(() => import("./codemirror"), {
  ssr: false,
  loading: () => <Loading label="Loading editor…" />,
});
export function NoteWorkbench({ note }: { note: Note }) {
  const api = useApi(),
    cache = useQueryClient(),
    w = useWorkspace();
  const { prefs } = usePreferences();
  const issueQuery = useQuery({
    queryKey: ["knowledge-issues", { kind: "note", noteId: note.id }],
    queryFn: ({ signal }) =>
      api.knowledgeIssues({ kind: "note", noteId: note.id }, signal),
    staleTime: 60_000,
    enabled: prefs.aiEnabled,
  });
  const issueCount =
    issueQuery.data?.issues.filter((issue) => issue.state === "open").length ??
    0;
  const overlapQuery = useQuery({
    queryKey: ["knowledge-overlap", { kind: "note", noteId: note.id }],
    queryFn: ({ signal }) =>
      api.knowledgeOverlap({ kind: "note", noteId: note.id }, signal),
    staleTime: 60_000,
    enabled: prefs.aiEnabled,
  });
  const overlapCount =
    overlapQuery.data?.findings.filter((item) => item.state === "open")
      .length ?? 0;
  const linkQuery = useQuery({
    queryKey: ["link-opportunities", { kind: "note", noteId: note.id }],
    queryFn: ({ signal }) =>
      api.linkOpportunities({ kind: "note", noteId: note.id }, signal),
    staleTime: 60_000,
    enabled: prefs.aiEnabled,
  });
  const linkCount =
    linkQuery.data?.suggestions.filter((item) => item.status === "open")
      .length ?? 0;
  const { update } = w;
  const [base, setBase] = useState(note),
    [source, setSource] = useState(note.markdown),
    [mode, setMode] = useState<"write" | "preview" | "split">(() =>
      takeEditingRequest(note.id) ? "write" : prefs.mode,
    ),
    [saving, setSaving] = useState(false),
    [error, setError] = useState<unknown>(null),
    [recovery, setRecovery] = useState<Draft | null>(null),
    [checked, setChecked] = useState(false),
    [conflict, setConflict] = useState(false);
  const [answerOpen, setAnswerOpen] = useState(false);
  const [wikiExportOpen, setWikiExportOpen] = useState(false);
  const [linkPanelOpen, setLinkPanelOpen] = useState(false);
  const [answer, setAnswer] = useState("");
  const editor = useRef<EditorView | null>(null);
  const [editorLoaded, setEditorLoaded] = useState(false);
  const savingLock = useRef(false);
  const saveAction = useRef<() => Promise<void>>(async () => {});
  const { register } = useCommandRuntime();
  const incoming = useRef(note);
  const key = `${w.scope}:${note.id}`;
  const links = useQuery({
    queryKey: ["links", note.id],
    queryFn: ({ signal }) => api.links(note.id, signal),
  });
  const dirty = source !== base.markdown;
  const questionNote = /^type:\s*["']?question["']?\s*$/im.test(base.markdown);
  const answered = /^status:\s*["']?answered["']?\s*$/im.test(base.markdown);
  const preview = useDeferredValue(source);
  const askSelection = useCallback(() => {
    if (!prefs.aiEnabled) return;
    const view = editor.current;
    const selection = view
      ? view.state.sliceDoc(
          view.state.selection.main.from,
          view.state.selection.main.to,
        )
      : (window.getSelection()?.toString() ?? "");
    openAskSlate({
      scope: "note",
      selectedText: selection.trim().slice(0, 4_000) || undefined,
    });
  }, [prefs.aiEnabled]);
  const [uploading, setUploading] = useState(false);
  const uploadIds = useRef(new WeakMap<File, string>());
  useEffect(() => {
    if (incoming.current === note) return;
    incoming.current = note;
    if (!dirty && !saving && !recovery) {
      setBase(note);
      setSource(note.markdown);
    }
  }, [note, dirty, saving, recovery]);
  useEffect(() => {
    recordRecent(w.scope, { id: note.id, path: note.path, title: note.title });
  }, [w.scope, note.id, note.path, note.title]);
  useEffect(() => {
    let live = true;
    drafts
      .get(key)
      .then((d) => {
        if (live) {
          if (d && d.source !== note.markdown) setRecovery(d);
          setChecked(true);
        }
      })
      .catch((e) => {
        if (live) {
          setError(e);
          setChecked(true);
        }
      });
    return () => {
      live = false;
    };
  }, [key, note.markdown]);
  useEffect(() => {
    update({ ...base, title: note.title, path: note.path }, dirty);
  }, [base, dirty, note.path, note.title, update]);
  const change = useCallback(
    (value: string) => {
      setSource(value);
      void drafts
        .put({
          key,
          id: base.id,
          path: note.path,
          baseRevision: base.revision,
          baseSource: base.markdown,
          source: value,
          updatedAt: new Date().toISOString(),
        })
        .catch(() =>
          setError(
            new Error(
              "Browser storage is unavailable. Keep this tab open and copy your text before leaving.",
            ),
          ),
        );
    },
    [base, key, note.path],
  );
  async function save() {
    if (
      savingLock.current ||
      saving ||
      uploading ||
      !checked ||
      recovery ||
      !dirty
    )
      return;
    savingLock.current = true;
    setSaving(true);
    setError(null);
    try {
      const saved = await api.save(base, source);
      setBase(saved);
      setSource(saved.markdown);
      await drafts.remove(key);
      cache.setQueryData(["note", note.id], saved);
      void cache.invalidateQueries({ queryKey: ["folder"] });
      void cache.invalidateQueries({ queryKey: ["search"] });
      void cache.invalidateQueries({ queryKey: ["smart-view"] });
      void cache.invalidateQueries({ queryKey: ["links", note.id] });
      setConflict(false);
    } catch (e) {
      setError(e);
      if (e instanceof ApiError && e.status === 412) setConflict(true);
    } finally {
      savingLock.current = false;
      setSaving(false);
    }
  }
  saveAction.current = save;
  const [pendingQuickSave, setPendingQuickSave] = useState(false);
  useEffect(() => {
    if (!pendingQuickSave) return;
    setPendingQuickSave(false);
    void saveAction.current();
  }, [pendingQuickSave, source]);
  useEffect(() => {
    const actions: Record<string, () => void | Promise<void>> = {
      "document.save": () => saveAction.current(),
      "document.write": () => setMode("write"),
      "document.read": () => setMode("preview"),
      "document.split": () => setMode("split"),
      "document.export-wiki": () => setWikiExportOpen(true),
      "document.focus-editor": () => {
        setMode("write");
        requestAnimationFrame(() => editor.current?.focus());
      },
      "document.ask-selection": askSelection,
      "document.link-opportunities": () => {
        setLinkPanelOpen(true);
        void linkQuery.refetch();
      },
    };
    if (questionNote && !answered && !dirty && !saving)
      actions["document.answer-question"] = () => setAnswerOpen(true);
    return register(`note:${note.id}`, actions);
  }, [
    answered,
    askSelection,
    dirty,
    linkQuery,
    note.id,
    questionNote,
    register,
    saving,
  ]);
  const insertSuggestedLink = useCallback(
    (suggestion: LinkSuggestion) => {
      const current = editor.current?.state.doc.toString() ?? source;
      const result = applyLinkSuggestionDraft(
        current,
        base.revision,
        suggestion,
      );
      if (!result.ok) return result.reason;
      setMode("write");
      if (editor.current)
        editor.current.dispatch({
          changes: { from: result.from, to: result.to, insert: result.insert },
          selection: { anchor: result.from + result.insert.length },
        });
      else change(result.markdown);
      return null;
    },
    [base.revision, change, source],
  );
  async function upload(files: File[]) {
    if (uploading || saving || !checked || recovery) return;
    setUploading(true);
    setError(null);
    try {
      let appended = source;
      if (files.length > 8)
        throw new Error("Choose at most eight files at a time.");
      for (const file of files) {
        if (file.size > 25 * 1024 * 1024)
          throw new Error("Each attachment must be 25 MB or smaller.");
        let id = uploadIds.current.get(file);
        if (!id) {
          id = crypto.randomUUID();
          uploadIds.current.set(file, id);
        }
        const asset = await api.upload(file, id);
        const text = "\n" + assetMarkdown(note.path, asset) + "\n";
        const view = editor.current;
        if (view) {
          const { from, to } = view.state.selection.main;
          view.dispatch({
            changes: { from, to, insert: text },
            selection: { anchor: from + text.length },
          });
        } else {
          appended += text;
          change(appended);
        }
      }
      setMode("write");
    } catch (e) {
      setError(e);
    } finally {
      setUploading(false);
    }
  }
  const formats: [Format, typeof Bold, string][] = [
    ["heading", Heading2, "Heading"],
    ["bold", Bold, "Bold"],
    ["italic", Italic, "Italic"],
    ["code", Code, "Inline code"],
    ["fence", SquareCode, "Code block"],
    ["quote", Quote, "Quote"],
    ["list", List, "Bullet list"],
    ["ordered", ListOrdered, "Numbered list"],
    ["task", ListTodo, "Task list"],
    ["link", Link, "Markdown link"],
    ["wiki", Braces, "Wiki link"],
  ];
  async function format(kind: Format) {
    if (mode === "preview") setMode("write");
    const { formatSelection } = await import("./codemirror");
    if (editor.current) formatSelection(editor.current, kind);
  }
  // Smart views read the frontmatter status; these actions set it without opening the editor.
  const status = frontmatterField(source, "status")?.toLowerCase() ?? null;
  const setStatus = (value: string | null) => {
    const next = setFrontmatterField(source, "status", value);
    if (next === source) return;
    const clean = !dirty;
    change(next);
    // A note with no other edits saves the status straight away; otherwise it joins the pending edits.
    if (clean) setPendingQuickSave(true);
  };
  const statusActions: ItemMenuAction[] = [
    ...(status !== "learning"
      ? [{ label: "Mark as currently learning", icon: GraduationCap, disabled: saving || !checked || !!recovery, onSelect: () => setStatus("learning") }]
      : []),
    ...(status !== "needs-review"
      ? [{ label: "Mark as needs review", icon: ListChecks, disabled: saving || !checked || !!recovery, onSelect: () => setStatus("needs-review") }]
      : []),
    ...(status && ["learning", "currently-learning", "in-progress", "needs-review", "review"].includes(status)
      ? [{ label: "Clear status", icon: CircleSlash, disabled: saving || !checked || !!recovery, onSelect: () => setStatus(null) }]
      : []),
  ];
  // Ticking a task while reading edits that one marker; like status changes it saves at once when nothing else is pending.
  const toggleTask = (offset: number) => {
    if (preview !== source || saving || !checked || recovery) return;
    const next = toggleTaskAt(source, offset);
    if (next === null) return;
    const clean = !dirty;
    change(next);
    if (clean) setPendingQuickSave(true);
  };
  const words = source.trim().split(/\s+/).filter(Boolean).length;
  const folders = note.path.split("/").slice(0, -1);
  const noteActions: ItemMenuAction[] = [
    ...(questionNote && !answered
      ? [
          {
            label: "Answer question",
            icon: MessageCircleQuestion,
            disabled: dirty || saving,
            onSelect: () => setAnswerOpen(true),
          },
        ]
      : []),
    ...(prefs.aiEnabled
      ? [
          {
            label: "Evolution of this note",
            icon: History,
            onSelect: () => w.openEvolution({ kind: "note", noteId: note.id }),
          },
        ]
      : []),
    ...statusActions,
    {
      label: "Export wiki links",
      icon: FileOutput,
      disabled: dirty || saving,
      onSelect: () => setWikiExportOpen(true),
    },
  ];
  return (
    <>
      <div className="editor-toolbar note-toolbar">
        <div className="mode-switch" role="group" aria-label="View mode">
          {(
            [
              ["preview", Eye, "Read"],
              ["write", Pencil, "Write"],
              ["split", Columns2, "Split"],
            ] as const
          ).map(([value, Icon, label]) => (
            <button
              key={value}
              type="button"
              aria-label={label}
              aria-pressed={mode === value}
              className={`mode-button ${mode === value ? "active" : ""} ${value === "split" ? "desktop-only" : ""}`}
              onClick={() => setMode(value)}
            >
              <Icon size={15} />
              <span>{label}</span>
            </button>
          ))}
        </div>
        {mode === "preview" ? (
          <p className="toolbar-path" title={note.path}>
            {folders.length ? folders.join(" / ") : "Library"}
          </p>
        ) : (
          <div className="format-tools">
            {formats.map(([kind, Icon, label]) => (
              <IconButton
                key={kind}
                label={label}
                disabled={saving}
                onClick={() => format(kind)}
              >
                <Icon size={16} />
              </IconButton>
            ))}
            <span className="toolbar-divider" />
            <UploadTools onFiles={upload} busy={uploading || saving} />
          </div>
        )}
        <div className="toolbar-end">
          {prefs.aiEnabled && (
            <div className="note-insights">
              <IconButton label="Ask about selection" onClick={askSelection}>
                <Sparkles size={16} />
              </IconButton>
              {issueCount > 0 && (
                <button
                  className="note-issue-indicator"
                  title="Knowledge issues"
                  onClick={() =>
                    w.openKnowledgeIssues({ kind: "note", noteId: note.id })
                  }
                  aria-label={`${issueCount} knowledge issue${issueCount === 1 ? "" : "s"} for this note`}
                >
                  <ShieldQuestion size={14} />
                  {issueCount}
                </button>
              )}
              {overlapCount > 0 && (
                <button
                  className="note-issue-indicator"
                  title="Overlapping notes"
                  onClick={() =>
                    w.openKnowledgeOverlap({ kind: "note", noteId: note.id })
                  }
                  aria-label={`${overlapCount} overlap suggestion${overlapCount === 1 ? "" : "s"} for this note`}
                >
                  <Layers3 size={14} />
                  {overlapCount}
                </button>
              )}
              {linkCount > 0 && (
                <button
                  className="note-issue-indicator note-link-control"
                  title="Link opportunities"
                  onClick={() => setLinkPanelOpen(true)}
                  aria-label={`${linkCount} link opportunit${linkCount === 1 ? "y" : "ies"} for this note`}
                >
                  <Link2 size={14} />
                  {linkCount}
                </button>
              )}
            </div>
          )}
          <span className="save-status" aria-live="polite">
            <span className="word-count">
              {words.toLocaleString()} word{words === 1 ? "" : "s"} ·{" "}
            </span>
            <span>{saving ? "Saving…" : dirty ? "Unsaved changes" : "Saved"}</span>
          </span>
          <Button
            size="sm"
            variant={dirty ? "default" : "ghost"}
            className="save-button"
            aria-label="Save note"
            title="Save note (Ctrl S)"
            disabled={!dirty || saving || uploading || !checked || !!recovery}
            onClick={save}
          >
            <Save size={15} />
            <span>Save</span>
          </Button>
          <ItemMenu
            entry={{
              name: note.path.split("/").at(-1)!,
              path: note.path,
              isDirectory: false,
              id: note.id,
              title: note.title,
            }}
            extra={noteActions}
          />
        </div>
      </div>
      {error != null && <ErrorMessage error={error} />}{" "}
      {conflict && (
        <div className="conflict-banner">
          <span>
            Your draft is safe. Compare with the current server version before
            making another edit.
          </span>
          <Button
            variant="outline"
            size="sm"
            onClick={async () => {
              try {
                const latest = await api.note(note.id);
                setRecovery({
                  key,
                  id: note.id,
                  path: note.path,
                  baseRevision: latest.revision,
                  baseSource: latest.markdown,
                  source,
                  updatedAt: new Date().toISOString(),
                });
                setBase(latest);
              } catch (e) {
                setError(e);
              }
            }}
          >
            Review latest
          </Button>
        </div>
      )}
      {mode === "write" && <h1 className="sr-only">{note.title}</h1>}
      <div className={`document-content mode-${mode}`}>
        <div className="editor-pane" hidden={mode === "preview"}>
          {(mode !== "preview" || editorLoaded) && (
            <Editor
              fontSize={prefs.fontSize}
              lineNumbers={prefs.lineNumbers}
              wrap={prefs.wrap}
              value={source}
              onChange={change}
              onSave={save}
              onReady={(v) => {
                editor.current = v;
                setEditorLoaded(true);
              }}
              onFiles={upload}
              readOnly={saving || uploading || !checked || !!recovery}
            />
          )}
        </div>
        {mode !== "write" && (
          <div className="reader-scroll">
            <header className="reader-title">
              <h1>{note.title}</h1>
            </header>
            <Markdown
              source={preview}
              links={links.data}
              title={note.title}
              path={note.path}
              onToggleTask={toggleTask}
            />
          </div>
        )}
      </div>
      <Modal
        open={!!recovery}
        onClose={() => undefined}
        dismissible={false}
        title={conflict ? "Review both versions" : "Recover your draft"}
        description="Nothing is written to the server until you explicitly save."
        wide
      >
        {recovery && (
          <>
            <div className="recovery-columns">
              <div>
                <h3>Server version</h3>
                <pre>{base.markdown}</pre>
              </div>
              <div>
                <h3>Your browser draft</h3>
                <pre>{recovery.source}</pre>
              </div>
            </div>
            <div className="dialog-actions">
              <Button
                variant="outline"
                onClick={async () => {
                  await drafts.remove(key);
                  setSource(base.markdown);
                  setRecovery(null);
                  setConflict(false);
                }}
              >
                Use server version
              </Button>
              <Button
                onClick={() => {
                  change(recovery.source);
                  setRecovery(null);
                  setMode("write");
                  setConflict(false);
                }}
              >
                Continue editing draft
              </Button>
            </div>
          </>
        )}
      </Modal>
      <Modal
        open={answerOpen}
        onClose={() => !saving && setAnswerOpen(false)}
        title="Answer question"
        description="Your answer will be appended to this note and its status will become answered."
      >
        <form
          className="form-stack"
          onSubmit={async (event) => {
            event.preventDefault();
            setSaving(true);
            setError(null);
            try {
              const saved = await api.answer(note.id, answer, base.revision);
              setBase(saved);
              setSource(saved.markdown);
              cache.setQueryData(["note", note.id], saved);
              void cache.invalidateQueries({ queryKey: ["smart-view"] });
              void cache.invalidateQueries({ queryKey: ["search"] });
              setAnswer("");
              setAnswerOpen(false);
            } catch (error) {
              setError(error);
            } finally {
              setSaving(false);
            }
          }}
        >
          <label>
            Answer
            <textarea
              autoFocus
              required
              rows={8}
              value={answer}
              onChange={(event) => setAnswer(event.target.value)}
              placeholder="Write the answer you want to keep with this question."
            />
          </label>
          <div className="dialog-actions">
            <Button
              type="button"
              variant="outline"
              onClick={() => setAnswerOpen(false)}
            >
              Cancel
            </Button>
            <Button type="submit" disabled={saving || !answer.trim()}>
              {saving ? "Saving…" : "Save answer"}
            </Button>
          </div>
        </form>
      </Modal>
      {wikiExportOpen && (
        <WikiExportDialog
          note={note}
          onClose={() => setWikiExportOpen(false)}
          onApplied={async (saved) => {
            setBase(saved);
            setSource(saved.markdown);
            cache.setQueryData(["note", note.id], saved);
            await drafts.remove(key);
            void cache.invalidateQueries({ queryKey: ["links", note.id] });
            void cache.invalidateQueries({ queryKey: ["search"] });
            setWikiExportOpen(false);
          }}
        />
      )}
      <Modal
        open={linkPanelOpen}
        onClose={() => setLinkPanelOpen(false)}
        title="Link opportunities"
        description="Insertions become unsaved editor changes. Save normally when you are ready."
      >
        {linkQuery.isPending ? (
          <Loading label="Checking this note for missing links…" />
        ) : linkQuery.error ? (
          <ErrorMessage
            error={linkQuery.error}
            retry={() => linkQuery.refetch()}
          />
        ) : linkQuery.data ? (
          <LinkOpportunityList
            snapshot={linkQuery.data}
            compact
            onInsert={insertSuggestedLink}
          />
        ) : null}
      </Modal>
    </>
  );
}
