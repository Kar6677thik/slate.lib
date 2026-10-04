"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  FileText,
  Folder,
  Search,
  Star,
  X,
  Atom,
} from "lucide-react";
import { useApi } from "@/lib/auth/context";
import { useWorkspace } from "@/features/notes/workspace-context";
import { useWorkspacePreferences } from "@/lib/storage/workspace-preferences";
import { readRecent } from "@/lib/storage/recent";
import { useDebounce } from "@/hooks/use-debounce";
import { Modal } from "@/components/common/primitives";
import { useCommandRegistry } from "./use-command-registry";
import { fuzzyScore } from "./ranking";
import { collectCommandSources, commandResults } from "./registry";
import { readCommandHistory, recordCommandUse } from "@/lib/storage/command-history";
import type { CommandGroup, CommandResult } from "./types";

const groupOrder: CommandGroup[] = [
  "Create",
  "Open tabs",
  "Favorites",
  "Recent notes",
  "Notes",
  "Concepts",
  "Intelligence",
  "Navigation",
  "Document",
  "Workspace",
  "Library",
  "Pinned folders",
  "Saved searches",
  "Appearance",
  "Settings",
];

function cleanText(value?: string) {
  return value?.replace(/<[^>]*>/g, "").replace(/\s+/g, " ").trim();
}

export function CommandCenter() {
  const workspace = useWorkspace();
  const api = useApi();
  const local = useWorkspacePreferences(workspace.scope);
  const { commands } = useCommandRegistry();
  const [query, setQuery] = useState("");
  const [active, setActive] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [historyVersion, setHistoryVersion] = useState(0);
  const input = useRef<HTMLInputElement>(null);
  const commandOnly = query.trimStart().startsWith(">");
  const term = query.trimStart().replace(/^>\s*/, "");
  const debounced = useDebounce(term, 160);
  const remote = useQuery({
    queryKey: ["command-search", debounced],
    queryFn: async ({ signal }) => {
      try { return await api.hybridSearch(debounced, "hybrid", 0, signal); }
      catch { return api.search(debounced, 0, signal); }
    },
    enabled: workspace.searchOpen && !commandOnly && !!debounced,
    staleTime: 20_000,
  });
  const conceptIndex = useQuery({
    queryKey: ["command-concepts"],
    queryFn: ({ signal }) => api.concepts({}, signal) as Promise<import("@/lib/intelligence/concepts").ConceptSnapshot>,
    enabled: workspace.searchOpen && !commandOnly && debounced.length >= 2,
    staleTime: 60_000,
    retry: false,
  });

  useEffect(() => {
    if (workspace.searchOpen) {
      setQuery(workspace.commandInitialQuery);
      setActive(0);
      setError(null);
      requestAnimationFrame(() => input.current?.focus());
    }
  }, [workspace.commandInitialQuery, workspace.searchOpen]);

  const results = useMemo(() => {
    void historyVersion;
    const commandHistory = readCommandHistory(workspace.scope);
    const recentIds = commandHistory.map((item) => item.id);
    const output = collectCommandSources(
      [{
        id: "registered-commands",
        collect: (value) => commandResults(commands, value, recentIds),
      }],
      term,
    );

    if (!commandOnly) {
      const addLocalNote = (
        group: CommandGroup,
        note: { id: string; title: string; path: string },
        base: number,
        icon = FileText,
      ) => {
        const score = fuzzyScore(term, [note.title, note.path]);
        if (term && !score) return;
        output.push({
          key: `${group}:${note.id}`,
          title: note.title,
          path: note.path,
          icon,
          group,
          score: base + (term ? score : 0),
          execute: () => workspace.open(note.id),
        });
      };
      workspace.tabs.forEach((tab, index) =>
        addLocalNote("Open tabs", tab, 74 - index),
      );
      local.preferences.favorites.forEach((note, index) =>
        addLocalNote("Favorites", note, 68 - index, Star),
      );
      readRecent(workspace.scope).forEach((note, index) =>
        addLocalNote("Recent notes", note, 58 - index / 4),
      );
      local.preferences.searches.forEach((saved, index) => {
        const score = fuzzyScore(term, [saved.name, saved.query]);
        if (term && !score) return;
        output.push({
          key: `search:${saved.id}`,
          title: saved.name,
          description: saved.query,
          icon: Search,
          group: "Saved searches",
          score: 52 - index + (term ? score : 0),
          execute: () => {
            workspace.setQuery(saved.query);
            workspace.navigate("search");
          },
        });
      });
      local.preferences.pins.forEach((pin, index) => {
        const score = fuzzyScore(term, [pin.label, pin.path]);
        if (term && !score) return;
        output.push({
          key: `folder:${pin.path}`,
          title: pin.label,
          path: pin.path,
          icon: Folder,
          group: "Pinned folders",
          score: 46 - index + (term ? score : 0),
          execute: () => workspace.navigate("library"),
        });
      });
      output.push(
        ...collectCommandSources(
          [{
            id: "server-notes",
            collect: () => term && remote.data
              ? remote.data.results.map<CommandResult>((note, index) => ({
            key: `note:${note.id}`,
            title: note.title,
            description: cleanText(note.snippet),
            path: note.path,
            icon: FileText,
            group: "Notes",
            score: 240 + (note.score ?? 0) - index / 10,
            execute: () => workspace.open(note.id),
              }))
              : [],
          }],
          term,
        ),
      );
      if (term && conceptIndex.data) {
        const normalized = term.toLowerCase();
        output.push(...conceptIndex.data.concepts.flatMap((concept, index) => {
          const score = fuzzyScore(term, [concept.canonicalName, ...concept.aliases, ...concept.tags]);
          if (!score && !concept.normalizedName.includes(normalized)) return [];
          return [{ key: `concept:${concept.id}`, title: `Concept · ${concept.canonicalName}`, description: `${concept.kind} · ${concept.sourceCount} sources`, icon: Atom, group: "Concepts" as const, score: 190 + score - index / 100, execute: () => workspace.openConcept(concept.canonicalName) }];
        }));
      }
    }

    const deduped = new Map<string, CommandResult>();
    for (const result of output) {
      const noteId = result.key.split(":").at(-1)!;
      const key = result.commandId ? result.key : `${result.title}:${noteId}`;
      const existing = deduped.get(key);
      if (!existing || existing.score < result.score) deduped.set(key, result);
    }
    return [...deduped.values()]
      .sort((a, b) => {
        if (!term) {
          const group = groupOrder.indexOf(a.group) - groupOrder.indexOf(b.group);
          if (group) return group;
        }
        return b.score - a.score || a.title.localeCompare(b.title);
      })
      .slice(0, term ? 80 : 36);
  }, [
    commandOnly,
    commands,
    conceptIndex.data,
    historyVersion,
    local.preferences,
    remote.data,
    term,
    workspace,
  ]);

  useEffect(() => setActive(0), [query, results.length]);

  async function choose(result: CommandResult) {
    setError(null);
    try {
      await result.execute();
      if (result.commandId) {
        recordCommandUse(workspace.scope, result.commandId);
        setHistoryVersion((value) => value + 1);
      }
      workspace.setSearchOpen(false);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "That action could not be completed.");
    }
  }

  return (
    <Modal
      open={workspace.searchOpen}
      onClose={() => workspace.setSearchOpen(false)}
      title="Command Center"
      description="Search notes and run workspace commands."
      wide
    >
      <div className="command-center">
        <div className="command-input">
          <Search size={19} aria-hidden="true" />
          <input
            ref={input}
            role="combobox"
            aria-label="Quick search for notes and commands"
            aria-controls="command-results"
            aria-expanded="true"
            aria-autocomplete="list"
            aria-activedescendant={results[active] ? `command-result-${active}` : undefined}
            autoComplete="off"
            spellCheck={false}
            placeholder="Search notes or type > for commands"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === "ArrowDown") {
                event.preventDefault();
                setActive((value) => Math.min(results.length - 1, value + 1));
              } else if (event.key === "ArrowUp") {
                event.preventDefault();
                setActive((value) => Math.max(0, value - 1));
              } else if (event.key === "Home") {
                event.preventDefault();
                setActive(0);
              } else if (event.key === "End") {
                event.preventDefault();
                setActive(Math.max(0, results.length - 1));
              } else if (event.key === "Enter" && results[active]) {
                event.preventDefault();
                void choose(results[active]);
              }
            }}
          />
          {query && (
            <button className="command-clear" aria-label="Clear search" onClick={() => setQuery("")}>
              <X size={16} />
            </button>
          )}
          <span className="command-mode">{commandOnly ? "Commands" : "All"}</span>
        </div>
        {error && <div className="command-error" role="alert">{error}</div>}
        <div
          id="command-results"
          className="quick-results command-results"
          role="listbox"
          aria-label="Command Center results"
        >
          {results.map((result, index) => {
            const previous = results[index - 1];
            const Icon = result.icon;
            return (
              <div className="command-result-wrap" key={result.key}>
                {previous?.group !== result.group && (
                  <div className="command-group" role="presentation">{result.group}</div>
                )}
                <button
                  id={`command-result-${index}`}
                  className={`command-result ${index === active ? "active" : ""} ${result.dangerous ? "dangerous" : ""}`}
                  role="option"
                  aria-selected={index === active}
                  onMouseEnter={() => setActive(index)}
                  onClick={() => void choose(result)}
                >
                  <Icon size={17} aria-hidden="true" />
                  <span className="command-result-copy">
                    <strong>{result.title}</strong>
                    {(result.path || result.description) && (
                      <small>{result.path ?? result.description}</small>
                    )}
                  </span>
                  {result.shortcut && <kbd aria-label={`Shortcut ${result.shortcut}`}>{result.shortcut}</kbd>}
                </button>
              </div>
            );
          })}
          {!results.length && !remote.isPending && (
            <div className="command-empty">No matching notes or commands.</div>
          )}
          {remote.isPending && term && !commandOnly && (
            <div className="command-searching" role="status">Searching library…</div>
          )}
          {remote.error && (
            <div className="command-searching" role="status">Server search is unavailable. Local commands still work.</div>
          )}
        </div>
        <div className="overlay-hint command-footer">
          <span><kbd>↑</kbd><kbd>↓</kbd> navigate</span>
          <span><kbd>Enter</kbd> open</span>
          <span><kbd>Esc</kbd> close</span>
          <span className="command-footer-mode"><kbd>&gt;</kbd> commands</span>
        </div>
      </div>
    </Modal>
  );
}
