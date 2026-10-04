"use client";

import { useCallback, useMemo } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useTheme } from "next-themes";
import { format } from "date-fns";
import {
  ArchiveRestore,
  BookOpen,
  CalendarDays,
  ClipboardCopy,
  Columns2,
  Compass,
  Copy,
  FileClock,
  FileOutput,
  FilePlus2,
  Folder,
  FolderOpen,
  FolderPlus,
  History,
  Inbox,
  Info,
  Link,
  ListFilter,
  Moon,
  MessageCircleQuestion,
  Network,
  PanelLeft,
  PanelRight,
  Pencil,
  RefreshCw,
  RotateCcw,
  Save,
  Search,
  Settings,
  Star,
  Sun,
  Trash2,
  Unlink,
  Waypoints,
  Zap,
  BrainCircuit,
  FileQuestion,
} from "lucide-react";
import { useApi } from "@/lib/auth/context";
import { useWorkspace } from "@/features/notes/workspace-context";
import { useWorkspacePreferences } from "@/lib/storage/workspace-preferences";
import { SMART_VIEWS } from "@/features/views/smart-views";
import { useCommandRuntime } from "./runtime";
import type { CommandDefinition } from "./types";
import { availableCommands } from "./registry";
import { openAskSlate } from "@/components/ask/ask-slate";

export function useCommandRegistry() {
  const api = useApi();
  const cache = useQueryClient();
  const workspace = useWorkspace();
  const local = useWorkspacePreferences(workspace.scope);
  const runtime = useCommandRuntime();
  const { theme, setTheme } = useTheme();
  const active = workspace.tabs.find((tab) => tab.id === workspace.active);
  const favorite = !!active && local.preferences.favorites.some((item) => item.id === active.id);
  const projectContext = workspace.projectPath || local.preferences.projects
    .filter((project) => active && (active.path === project.path || active.path.startsWith(`${project.path}/`)))
    .sort((a, b) => b.path.length - a.path.length)[0]?.path || (active?.path.includes("/") ? active.path.split("/").slice(0, -1).join("/") : "");
  const commands = useMemo<CommandDefinition[]>(() => {
    const entry = active
      ? {
          id: active.id,
          title: active.title,
          path: active.path,
          name: active.path.split("/").at(-1) ?? active.title,
          isDirectory: false,
        }
      : undefined;
    const showDetails = async (id: string) => {
      await runtime.run("workspace.show-details");
      await runtime.run(id);
    };
    const invalidateLibrary = () => {
      void cache.invalidateQueries({ queryKey: ["folder"] });
      void cache.invalidateQueries({ queryKey: ["search"] });
      void cache.invalidateQueries({ queryKey: ["smart-view"] });
    };
    const navigate = (
      id: string,
      title: string,
      destination: Parameters<typeof workspace.navigate>[0],
      icon: CommandDefinition["icon"],
      keywords: string[] = [],
    ): CommandDefinition => ({
      id,
      title,
      icon,
      keywords,
      group: "Navigation",
      execute: () => workspace.navigate(destination),
    });
    const result: CommandDefinition[] = [
      {
        id: "create.note",
        title: "New note",
        description: "Create a Markdown note in the library",
        icon: FilePlus2,
        keywords: ["create", "write", "document"],
        group: "Create",
        shortcut: "Ctrl N",
        execute: () => workspace.setOperation({ kind: "create-note", folder: "" }),
      },
      {
        id: "create.folder",
        title: "New folder",
        description: "Add a folder to the library",
        icon: FolderPlus,
        keywords: ["create", "directory"],
        group: "Create",
        execute: () => workspace.setOperation({ kind: "create-folder", folder: "" }),
      },
      {
        id: "create.capture",
        title: "Quick Thought",
        description: "Capture a thought in the inbox",
        icon: Zap,
        keywords: ["capture", "inbox", "quick"],
        group: "Create",
        shortcut: "Ctrl Shift C",
        execute: () => workspace.setCaptureOpen(true),
      },
      {
        id: "create.daily",
        title: "Open daily note",
        description: "Create or open today’s note",
        icon: CalendarDays,
        keywords: ["today", "journal"],
        group: "Create",
        execute: async () => {
          const note = await api.daily(format(new Date(), "yyyy-MM-dd"));
          invalidateLibrary();
          workspace.open(note.id);
        },
      },
      {
        id: "create.question",
        title: "New question",
        description: "Create a question to answer later",
        icon: ListFilter,
        keywords: ["ask", "unanswered", "learning"],
        group: "Create",
        when: () => runtime.has("create.question"),
        execute: () => runtime.run("create.question"),
      },
      {
        id: "intelligence.ask-library",
        title: "Ask Slate",
        description: "Research your library with cited sources",
        icon: MessageCircleQuestion,
        keywords: ["ask", "assistant", "knowledge", "sources"],
        group: "Intelligence",
        execute: () => openAskSlate({ scope: "library" }),
      },
      {
        id: "intelligence.ask-note",
        title: "Ask Current Note",
        description: "Use only the active note as evidence",
        icon: MessageCircleQuestion,
        keywords: ["ask", "document", "current", "source"],
        group: "Intelligence",
        when: () => !!active,
        execute: () => openAskSlate({ scope: "note" }),
      },
      {
        id: "intelligence.ask-folder",
        title: "Ask Current Folder",
        description: "Research notes in the active folder",
        icon: MessageCircleQuestion,
        keywords: ["ask", "folder", "project", "scope"],
        group: "Intelligence",
        when: () => !!active?.path.includes("/"),
        execute: () => openAskSlate({ scope: "folder" }),
      },
      {
        id: "intelligence.ask-selected",
        title: "Ask Selected Notes",
        description: "Choose from currently open notes",
        icon: MessageCircleQuestion,
        keywords: ["ask", "selected", "tabs", "scope"],
        group: "Intelligence",
        when: () => workspace.tabs.length > 0,
        execute: () => openAskSlate({ scope: "selected" }),
      },
      {
        id: "evolution.open",
        title: "Evolution of Thought",
        description: "Trace how a topic changed across notes and committed history",
        icon: History,
        keywords: ["evolution", "timeline", "thinking", "changed"],
        group: "Intelligence",
        execute: () => workspace.openEvolution({ kind: "topic", topic: "" }),
      },
      {
        id: "evolution.current-note",
        title: "Evolution of Current Note",
        description: "Compare meaningful versions of the active note",
        icon: FileClock,
        keywords: ["history", "compare", "before", "after"],
        group: "Intelligence",
        when: () => !!active,
        execute: () => active && workspace.openEvolution({ kind: "note", noteId: active.id }),
      },
      {
        id: "evolution.selected-notes",
        title: "Evolution of Open Notes",
        description: "Compare the history of currently open notes",
        icon: FileClock,
        keywords: ["selected", "tabs", "compare", "history"],
        group: "Intelligence",
        when: () => workspace.tabs.length > 0,
        execute: () => workspace.openEvolution({ kind: "selected", noteIds: workspace.tabs.slice(0, 20).map((tab) => tab.id) }),
      },
      {
        id: "evolution.compare-earlier",
        title: "Compare Earlier Thinking",
        icon: History,
        keywords: ["current", "original", "version", "diff"],
        group: "Intelligence",
        when: () => !!active,
        execute: () => active && workspace.openEvolution({ kind: "note", noteId: active.id }),
      },
      {
        id: "evolution.project",
        title: "Project Evolution",
        description: "Trace decisions, architecture, questions, and direction",
        icon: History,
        keywords: ["project", "timeline", "decisions", "architecture"],
        group: "Intelligence",
        when: () => !!projectContext,
        execute: () => workspace.openEvolution({ kind: "project", path: projectContext }),
      },
      {
        id: "project.open-brain",
        title: "Open Project Brain",
        description: "Open the grounded workspace for this project folder",
        icon: BrainCircuit,
        keywords: ["project", "folder", "overview", "brain"],
        group: "Intelligence",
        when: () => !!projectContext,
        execute: () => workspace.openProjectBrain(projectContext),
      },
      {
        id: "project.resume",
        title: "Resume Project",
        description: "Build a cited context pack for continuing this project",
        icon: RotateCcw,
        keywords: ["project", "continue", "context", "summary"],
        group: "Intelligence",
        when: () => !!projectContext,
        execute: () => workspace.openProjectBrain(projectContext, "resume"),
      },
      {
        id: "project.questions",
        title: "Project Questions",
        icon: FileQuestion,
        keywords: ["project", "open", "unresolved"],
        group: "Intelligence",
        when: () => !!projectContext,
        execute: () => workspace.openProjectBrain(projectContext, "questions"),
      },
      {
        id: "project.timeline",
        title: "Project Timeline",
        icon: History,
        keywords: ["project", "history", "changes"],
        group: "Intelligence",
        when: () => !!projectContext,
        execute: () => workspace.openProjectBrain(projectContext, "timeline"),
      },
      {
        id: "project.graph",
        title: "Project Graph",
        icon: Network,
        keywords: ["project", "links", "structure"],
        group: "Intelligence",
        when: () => !!projectContext,
        execute: () => workspace.openProjectBrain(projectContext, "graph"),
      },
      {
        id: "project.ask",
        title: "Ask this project",
        icon: MessageCircleQuestion,
        keywords: ["project", "ask", "sources"],
        group: "Intelligence",
        when: () => !!projectContext,
        execute: () => openAskSlate({ scope: "project", path: projectContext }),
      },
      navigate("navigate.library", "Library", "library", FolderOpen, ["files", "folders"]),
      navigate("navigate.search", "Search library", "search", Search, ["find", "notes"]),
      navigate("navigate.inbox", "Inbox", "inbox", Inbox, ["capture"]),
      navigate("navigate.recent", "Recent notes", "recent", FileClock, ["history", "opened"]),
      navigate("navigate.favorites", "Favorites", "favorites", Star, ["bookmarks", "starred"]),
      navigate("navigate.rediscover", "Rediscover notes", "rediscover", Compass, ["forgotten", "review"]),
      navigate("navigate.recovery", "Recover deleted notes", "recovery", ArchiveRestore, ["trash", "restore"]),
      navigate("navigate.link-health", "Link diagnostics", "link-health", Unlink, ["broken", "unresolved", "health"]),
      ...SMART_VIEWS.map<CommandDefinition>((view) => ({
        id: `navigate.${view.id}`,
        title: view.title,
        description: view.description,
        icon: view.icon,
        keywords: ["smart view", "filter", view.id],
        group: "Navigation",
        execute: () => workspace.navigate(view.id),
      })),
      {
        id: "document.save",
        title: "Save note",
        icon: Save,
        keywords: ["write", "persist"],
        group: "Document",
        shortcut: "Ctrl S",
        when: () => !!active?.dirty && runtime.has("document.save"),
        execute: () => runtime.run("document.save"),
      },
      {
        id: "document.write",
        title: "Switch to write mode",
        icon: Pencil,
        keywords: ["edit", "source"],
        group: "Document",
        when: () => runtime.has("document.write"),
        execute: () => runtime.run("document.write"),
      },
      {
        id: "document.read",
        title: "Switch to reading view",
        icon: BookOpen,
        keywords: ["preview", "read"],
        group: "Document",
        when: () => runtime.has("document.read"),
        execute: () => runtime.run("document.read"),
      },
      {
        id: "document.split",
        title: "Split editor and preview",
        icon: Columns2,
        keywords: ["side by side", "preview"],
        group: "Document",
        when: () => runtime.has("document.split"),
        execute: () => runtime.run("document.split"),
      },
      {
        id: "document.favorite",
        title: favorite ? "Remove from favorites" : "Add to favorites",
        icon: Star,
        keywords: ["bookmark", "star"],
        group: "Document",
        when: () => !!active,
        execute: () => active && local.toggleFavorite(active),
      },
      ...(["rename", "move", "copy", "duplicate"] as const).map<CommandDefinition>((kind) => ({
        id: `document.${kind}`,
        title: `${kind[0].toUpperCase()}${kind.slice(1)} note${kind === "move" || kind === "copy" ? " to…" : ""}`,
        icon: kind === "rename" ? Pencil : kind === "move" ? Folder : kind === "copy" ? ClipboardCopy : Copy,
        keywords: ["file", kind],
        group: "Document",
        when: () => !!entry,
        execute: () => entry && workspace.setOperation({ kind, entry }),
      })),
      {
        id: "document.delete",
        title: "Delete note",
        description: "Move the current note to recoverable history",
        icon: Trash2,
        keywords: ["remove", "trash"],
        group: "Document",
        dangerous: true,
        when: () => !!entry,
        execute: () => entry && workspace.setOperation({ kind: "delete", entry }),
      },
      {
        id: "document.answer-question",
        title: "Answer this question",
        icon: ListFilter,
        keywords: ["question", "resolve"],
        group: "Document",
        when: () => runtime.has("document.answer-question"),
        execute: () => runtime.run("document.answer-question"),
      },
      {
        id: "document.ask-selection",
        title: "Ask about selection",
        description: "Use selected text as explicit read-only context",
        icon: MessageCircleQuestion,
        keywords: ["ask", "selection", "explain", "relate"],
        group: "Intelligence",
        when: () => !!active && runtime.has("document.ask-selection"),
        execute: () => runtime.run("document.ask-selection"),
      },
      {
        id: "document.export-wiki",
        title: "Export wiki links",
        icon: FileOutput,
        keywords: ["markdown", "convert", "links"],
        group: "Document",
        when: () => runtime.has("document.export-wiki"),
        execute: () => runtime.run("document.export-wiki"),
      },
      {
        id: "document.backlinks",
        title: "Show backlinks",
        icon: Link,
        keywords: ["incoming", "links"],
        group: "Document",
        when: () => !!active && runtime.has("details.links"),
        execute: () => showDetails("details.links"),
      },
      {
        id: "document.outgoing-links",
        title: "Show outgoing links",
        icon: Link,
        keywords: ["resolved", "links"],
        group: "Document",
        when: () => !!active && runtime.has("details.links"),
        execute: () => showDetails("details.links"),
      },
      {
        id: "document.history",
        title: "Open version history",
        icon: History,
        keywords: ["restore", "compare", "versions"],
        group: "Document",
        when: () => !!active && runtime.has("details.history"),
        execute: () => showDetails("details.history"),
      },
      {
        id: "document.graph",
        title: "Open note graph",
        icon: Network,
        keywords: ["connections", "links"],
        group: "Document",
        when: () => !!active && runtime.has("details.graph"),
        execute: () => showDetails("details.graph"),
      },
      {
        id: "document.related",
        title: "Show related notes",
        icon: Waypoints,
        keywords: ["similar", "connections"],
        group: "Document",
        when: () => !!active && runtime.has("details.related"),
        execute: () => showDetails("details.related"),
      },
      {
        id: "document.info",
        title: "Show document information",
        icon: Info,
        keywords: ["metadata", "path", "size"],
        group: "Document",
        when: () => !!active && runtime.has("details.info"),
        execute: () => showDetails("details.info"),
      },
      {
        id: "workspace.next-tab",
        title: "Next tab",
        icon: RotateCcw,
        keywords: ["switch", "forward"],
        group: "Workspace",
        shortcut: "Ctrl Tab",
        when: () => workspace.tabs.length > 1,
        execute: () => {
          const index = workspace.tabs.findIndex((tab) => tab.id === workspace.active);
          workspace.open(workspace.tabs[(index + 1 + workspace.tabs.length) % workspace.tabs.length].id);
        },
      },
      {
        id: "workspace.previous-tab",
        title: "Previous tab",
        icon: RotateCcw,
        keywords: ["switch", "back"],
        group: "Workspace",
        shortcut: "Ctrl Shift Tab",
        when: () => workspace.tabs.length > 1,
        execute: () => {
          const index = workspace.tabs.findIndex((tab) => tab.id === workspace.active);
          workspace.open(workspace.tabs[(index - 1 + workspace.tabs.length) % workspace.tabs.length].id);
        },
      },
      {
        id: "workspace.close-tab",
        title: "Close tab",
        icon: Trash2,
        keywords: ["document", "tab"],
        group: "Workspace",
        shortcut: "Ctrl W",
        when: () => !!workspace.active,
        execute: () => workspace.active && workspace.close(workspace.active),
      },
      {
        id: "workspace.toggle-library",
        title: "Toggle library pane",
        icon: PanelLeft,
        keywords: ["sidebar", "files", "navigation"],
        group: "Workspace",
        when: () => runtime.has("workspace.toggle-library"),
        execute: () => runtime.run("workspace.toggle-library"),
      },
      {
        id: "workspace.toggle-details",
        title: "Toggle details pane",
        icon: PanelRight,
        keywords: ["right sidebar", "rail"],
        group: "Workspace",
        when: () => runtime.has("workspace.toggle-details"),
        execute: () => runtime.run("workspace.toggle-details"),
      },
      {
        id: "workspace.focus-editor",
        title: "Focus editor",
        icon: Pencil,
        keywords: ["write", "document"],
        group: "Workspace",
        when: () => runtime.has("document.focus-editor"),
        execute: () => runtime.run("document.focus-editor"),
      },
      {
        id: "workspace.focus-library",
        title: "Focus library",
        icon: PanelLeft,
        keywords: ["sidebar", "files"],
        group: "Workspace",
        when: () => runtime.has("workspace.focus-library"),
        execute: () => runtime.run("workspace.focus-library"),
      },
      {
        id: "library.refresh",
        title: "Refresh library",
        icon: RefreshCw,
        keywords: ["reload", "index"],
        group: "Library",
        execute: async () => {
          await api.refresh();
          invalidateLibrary();
        },
      },
      {
        id: "library.sync",
        title: "Sync library",
        icon: RefreshCw,
        keywords: ["git", "server", "synchronize"],
        group: "Library",
        execute: () => api.sync(),
      },
      {
        id: "appearance.light",
        title: "Use light theme",
        icon: Sun,
        keywords: ["appearance", "color"],
        group: "Appearance",
        when: () => theme !== "light",
        execute: () => setTheme("light"),
      },
      {
        id: "appearance.dark",
        title: "Use dark theme",
        icon: Moon,
        keywords: ["appearance", "color"],
        group: "Appearance",
        when: () => theme !== "dark",
        execute: () => setTheme("dark"),
      },
      {
        id: "appearance.system",
        title: "Use system theme",
        icon: Settings,
        keywords: ["appearance", "automatic"],
        group: "Appearance",
        when: () => theme !== "system",
        execute: () => setTheme("system"),
      },
      {
        id: "settings.open",
        title: "Open settings",
        icon: Settings,
        keywords: ["preferences", "connection", "editor"],
        group: "Settings",
        execute: () => workspace.setSettingsOpen(true),
      },
    ];
    return result;
  }, [
    active,
    api,
    cache,
    favorite,
    local,
    projectContext,
    runtime,
    setTheme,
    theme,
    workspace,
  ]);

  const available = useMemo(
    () => availableCommands(commands),
    [commands],
  );

  const executeById = useCallback(
    async (id: string) => {
      const command = commands.find((item) => item.id === id);
      if (!command || !(command.when?.() ?? true)) return false;
      await command.execute();
      return true;
    },
    [commands],
  );

  return { commands: available, executeById };
}
