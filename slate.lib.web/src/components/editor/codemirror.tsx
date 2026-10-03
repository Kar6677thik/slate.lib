"use client";
import { useEffect, useRef } from "react";
import { basicSetup } from "codemirror";
import { EditorView, keymap } from "@codemirror/view";
import { EditorState, Compartment } from "@codemirror/state";
import { markdown } from "@codemirror/lang-markdown";
import { indentWithTab } from "@codemirror/commands";
import { useTheme } from "next-themes";
export type Format =
  | "bold"
  | "italic"
  | "heading"
  | "code"
  | "fence"
  | "quote"
  | "list"
  | "ordered"
  | "task"
  | "link"
  | "wiki";
export function formatSelection(view: EditorView, format: Format) {
  const { from, to } = view.state.selection.main;
  const selected = view.state.sliceDoc(from, to);
  const pairs: Record<Format, [string, string, string]> = {
    bold: ["**", "**", "bold text"],
    italic: ["_", "_", "italic text"],
    heading: ["## ", "", "Heading"],
    code: ["`", "`", "code"],
    fence: ["\n```\n", "\n```\n", "code"],
    quote: ["> ", "", "Quote"],
    list: ["- ", "", "Item"],
    ordered: ["1. ", "", "Item"],
    task: ["- [ ] ", "", "Task"],
    link: ["[", "](https://)", "label"],
    wiki: ["[[", "]]", "Note title"],
  };
  const [left, right, fallback] = pairs[format];
  const content = selected || fallback;
  view.dispatch({
    changes: { from, to, insert: left + content + right },
    selection: {
      anchor: from + left.length,
      head: from + left.length + content.length,
    },
  });
  view.focus();
}
export default function MarkdownEditor({
  value,
  onChange,
  onSave,
  onReady,
  onFiles,
  readOnly = false,
  lineNumbers = true,
  wrap = true,
  fontSize = 15,
}: {
  value: string;
  onChange: (value: string) => void;
  onSave: () => void;
  onReady?: (view: EditorView | null) => void;
  onFiles?: (files: File[]) => void;
  readOnly?: boolean;
  lineNumbers?: boolean;
  wrap?: boolean;
  fontSize?: number;
}) {
  const host = useRef<HTMLDivElement>(null);
  const editor = useRef<EditorView | null>(null);
  const callbacks = useRef({ onChange, onSave, onReady, onFiles });
  callbacks.current = { onChange, onSave, onReady, onFiles };
  const initial = useRef(value);
  const compartment = useRef(new Compartment());
  const { resolvedTheme } = useTheme();
  useEffect(() => {
    if (!host.current) return;
    const view = new EditorView({
      parent: host.current,
      state: EditorState.create({
        doc: initial.current,
        extensions: [
          basicSetup,
          markdown(),
          keymap.of([
            {
              key: "Mod-s",
              run: () => {
                callbacks.current.onSave();
                return true;
              },
            },
            {
              key: "Mod-b",
              run: (v) => {
                formatSelection(v, "bold");
                return true;
              },
            },
            {
              key: "Mod-i",
              run: (v) => {
                formatSelection(v, "italic");
                return true;
              },
            },
            {
              key: "Mod-k",
              run: (v) => {
                formatSelection(v, "link");
                return true;
              },
            },
            indentWithTab,
          ]),
          compartment.current.of([]),
          EditorView.contentAttributes.of({
            "aria-label": "Markdown editor",
            spellcheck: "true",
          }),
          EditorView.updateListener.of((u) => {
            if (u.docChanged)
              callbacks.current.onChange(u.state.doc.toString());
          }),
          EditorView.domEventHandlers({
            paste: (e) => {
              const files = Array.from(e.clipboardData?.files ?? []);
              if (files.length && callbacks.current.onFiles) {
                e.preventDefault();
                callbacks.current.onFiles(files);
                return true;
              }
              return false;
            },
            drop: (e) => {
              const files = Array.from(e.dataTransfer?.files ?? []);
              if (files.length && callbacks.current.onFiles) {
                e.preventDefault();
                callbacks.current.onFiles(files);
                return true;
              }
              return false;
            },
          }),
        ],
      }),
    });
    editor.current = view;
    view.scrollDOM.tabIndex = 0;
    view.scrollDOM.setAttribute("aria-label", "Editor scrolling area");
    callbacks.current.onReady?.(view);
    return () => {
      callbacks.current.onReady?.(null);
      view.destroy();
      editor.current = null;
    };
  }, []);
  useEffect(() => {
    const view = editor.current;
    if (view && view.state.doc.toString() !== value)
      view.dispatch({
        changes: { from: 0, to: view.state.doc.length, insert: value },
      });
  }, [value]);
  useEffect(() => {
    editor.current?.dispatch({
      effects: compartment.current.reconfigure([
        EditorState.readOnly.of(readOnly),
        ...(wrap ? [EditorView.lineWrapping] : []),
        EditorView.theme(
          {
            "&": {
              height: "100%",
              fontSize: `${fontSize}px`,
              background: "var(--bg)",
              color: "var(--text)",
            },
            ".cm-content": {
              fontFamily: '"Cascadia Code", Consolas, monospace',
              padding: "24px 16px",
              caretColor: "var(--accent)",
            },
            ".cm-gutters": {
              backgroundColor: "var(--surface)",
              color: "var(--muted)",
              borderRight: "1px solid var(--border)",
              display: lineNumbers ? "flex" : "none",
            },
            ".cm-activeLine": { backgroundColor: "var(--accent-soft)" },
            ".cm-activeLineGutter": { backgroundColor: "var(--accent-soft)" },
            ".cm-scroller": { overflow: "auto" },
            "&.cm-focused": { outline: "none" },
            ".cm-selectionBackground, &.cm-focused .cm-selectionBackground": {
              backgroundColor:
                "color-mix(in srgb,var(--accent) 25%,transparent)",
            },
          },
          { dark: resolvedTheme === "dark" },
        ),
      ]),
    });
  }, [readOnly, lineNumbers, wrap, fontSize, resolvedTheme]);
  return <div className="code-editor" ref={host} />;
}
