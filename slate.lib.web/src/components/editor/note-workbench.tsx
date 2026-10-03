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
  Link,
  Braces,
  MessageCircleQuestion,
  FileOutput,
} from "lucide-react";
import type { EditorView } from "@codemirror/view";
import type { Note } from "@/lib/api/contracts";
import { useApi } from "@/lib/auth/context";
import { ApiError } from "@/lib/api/client";
import { useWorkspace } from "@/features/notes/workspace-context";
import { usePreferences } from "@/lib/storage/preferences";
import { recordRecent } from "@/lib/storage/recent";
import { drafts, type Draft } from "@/lib/storage/drafts";
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
import { ItemMenu } from "@/components/library/item-menu";
import type { Format } from "./codemirror";
import { WikiExportDialog } from "@/components/links/wiki-export-dialog";
import { useCommandRuntime } from "@/features/commands/runtime";
import { openAskSlate } from "@/components/ask/ask-slate";
const Editor = dynamic(() => import("./codemirror"), {
  ssr: false,
  loading: () => <Loading label="Loading editor…" />,
});
export function NoteWorkbench({ note }: { note: Note }) {
  const api = useApi(),
    cache = useQueryClient(),
    w = useWorkspace();
  const { prefs } = usePreferences();
  const { update } = w;
  const [base, setBase] = useState(note),
    [source, setSource] = useState(note.markdown),
    [mode, setMode] = useState<"write" | "preview" | "split">(prefs.mode),
    [saving, setSaving] = useState(false),
    [error, setError] = useState<unknown>(null),
    [recovery, setRecovery] = useState<Draft | null>(null),
    [checked, setChecked] = useState(false),
    [conflict, setConflict] = useState(false);
  const [answerOpen, setAnswerOpen] = useState(false);
  const [wikiExportOpen, setWikiExportOpen] = useState(false);
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
    const view = editor.current;
    const selection = view ? view.state.sliceDoc(view.state.selection.main.from, view.state.selection.main.to) : window.getSelection()?.toString() ?? "";
    openAskSlate({ scope: "note", selectedText: selection.trim().slice(0, 4_000) || undefined });
  }, []);
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
    };
    if (questionNote && !answered && !dirty && !saving)
      actions["document.answer-question"] = () => setAnswerOpen(true);
    return register(`note:${note.id}`, actions);
  }, [answered, askSelection, dirty, note.id, questionNote, register, saving]);
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
  return (
    <>
      <div className="note-header">
        <div className="note-heading-row">
          <div>
            <p className="breadcrumb">
              Library <span>/</span>
              {note.path}
            </p>
            <h1>{note.title}</h1>
          </div>
          <ItemMenu
            entry={{
              name: note.path.split("/").at(-1)!,
              path: note.path,
              isDirectory: false,
              id: note.id,
              title: note.title,
            }}
          />
        </div>
      </div>
      <div className="editor-toolbar">
        <div className="mode-switch">
          {(
            [
              ["write", Pencil, "Write"],
              ["preview", Eye, "Read"],
              ["split", Columns2, "Split"],
            ] as const
          ).map(([value, Icon, label]) => (
            <IconButton
              key={value}
              label={label}
              className={`${mode === value ? "active" : ""} ${value === "split" ? "desktop-only" : ""}`}
              onClick={() => setMode(value)}
            >
              <Icon size={17} />
            </IconButton>
          ))}
        </div>
        <span className="toolbar-divider" />
        <div className="format-tools" hidden={mode === "preview"}>
          {formats.map(([kind, Icon, label]) => (
            <IconButton
              key={kind}
              label={label}
              disabled={mode === "preview" || saving}
              onClick={() => format(kind)}
            >
              <Icon size={16} />
            </IconButton>
          ))}
        </div>
        <UploadTools onFiles={upload} busy={uploading || saving} />
        {questionNote && !answered && (
          <IconButton
            label="Answer question"
            disabled={dirty || saving}
            onClick={() => setAnswerOpen(true)}
          >
            <MessageCircleQuestion size={17} />
          </IconButton>
        )}
        <IconButton
          label="Ask about selection"
          onClick={askSelection}
        >
          <MessageCircleQuestion size={17} />
        </IconButton>
        <IconButton
          label="Export wiki links"
          disabled={dirty || saving}
          onClick={() => setWikiExportOpen(true)}
        >
          <FileOutput size={17} />
        </IconButton>
        <span className="save-status" aria-live="polite">
          {saving ? "Saving…" : dirty ? "Unsaved changes" : "Saved"}
        </span>
        <IconButton
          label="Save note"
          disabled={!dirty || saving || uploading || !checked || !!recovery}
          onClick={save}
        >
          <Save size={18} />
        </IconButton>
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
            <Markdown source={preview} links={links.data} title={note.title} />
          </div>
        )}
      </div>
      <footer className="document-footer">
        <span>
          {source.trim().split(/\s+/).filter(Boolean).length.toLocaleString()}{" "}
          words
        </span>
        <span>Markdown · UTF-8</span>
      </footer>
      <Modal
        open={!!recovery}
        onClose={() => setRecovery(null)}
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
            <Button type="button" variant="outline" onClick={() => setAnswerOpen(false)}>
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
    </>
  );
}
