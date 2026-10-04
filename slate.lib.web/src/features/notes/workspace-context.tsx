"use client";
import {
  createContext,
  useContext,
  useEffect,
  useState,
  useCallback,
  useRef,
  type ReactNode,
} from "react";
import type { Note, Entry } from "@/lib/api/contracts";
import type { Destination } from "@/components/layout/shell";
import { isSmartView } from "@/features/views/smart-views";
import type { EvolutionScope } from "@/lib/intelligence/evolution";
import type { KnowledgeSnapshot } from "@/lib/intelligence/knowledge-issues";
import type { OverlapSnapshot } from "@/lib/intelligence/overlap";
import type { LinkOpportunitySnapshot } from "@/lib/intelligence/smart-links";
import type { HealthFilters } from "@/lib/intelligence/health";
import type { GapFilters } from "@/lib/intelligence/knowledge-gaps";
export type Tab = { id: string; title: string; path: string; dirty: boolean };
export type Operation = {
  kind:
    | "create-note"
    | "create-folder"
    | "rename"
    | "move"
    | "copy"
    | "duplicate"
    | "delete";
  entry?: Entry;
  folder?: string;
};
type State = {
  scope: string;
  tabs: Tab[];
  active: string | null;
  nav: Destination;
  projectPath: string;
  projectSection: string | null;
  evolutionScope: EvolutionScope;
  knowledgeScope: KnowledgeSnapshot["scope"];
  overlapScope: OverlapSnapshot["scope"];
  linkScope: LinkOpportunitySnapshot["scope"];
  conceptIdentity: string;
  healthFilters: HealthFilters;
  gapFilters: GapFilters;
  query: string;
  searchOpen: boolean;
  commandInitialQuery: string;
  captureOpen: boolean;
  settingsOpen: boolean;
  operation: Operation | null;
  open: (id: string) => void;
  close: (id: string) => void;
  closePath: (path: string) => void;
  update: (note: Pick<Note, "id" | "title" | "path">, dirty?: boolean) => void;
  navigate: (nav: Destination) => void;
  openProjectBrain: (path: string, section?: string) => void;
  openEvolution: (scope: EvolutionScope) => void;
  openKnowledgeIssues: (scope?: KnowledgeSnapshot["scope"]) => void;
  openKnowledgeOverlap: (scope?: OverlapSnapshot["scope"]) => void;
  openLinkOpportunities: (scope?: LinkOpportunitySnapshot["scope"]) => void;
  openConcept: (identity?: string) => void;
  openLibraryHealth: (filters?: HealthFilters) => void;
  openKnowledgeGaps: (filters?: GapFilters) => void;
  setQuery: (query: string) => void;
  setSearchOpen: (v: boolean) => void;
  openCommandCenter: (query?: string) => void;
  setCaptureOpen: (v: boolean) => void;
  setSettingsOpen: (v: boolean) => void;
  setOperation: (op: Operation | null) => void;
};
const Context = createContext<State | null>(null);
export function WorkspaceProvider({
  scope,
  children,
}: {
  scope: string;
  children: ReactNode;
}) {
  const [tabs, setTabs] = useState<Tab[]>([]),
    [active, setActive] = useState<string | null>(null),
    [nav, setNav] = useState<Destination>("library"),
    [projectPath, setProjectPath] = useState(""),
    [projectSection, setProjectSection] = useState<string | null>(null),
    [evolutionScope, setEvolutionScope] = useState<EvolutionScope>({ kind: "topic", topic: "" }),
    [knowledgeScope, setKnowledgeScope] = useState<KnowledgeSnapshot["scope"]>({ kind: "library" }),
    [overlapScope, setOverlapScope] = useState<OverlapSnapshot["scope"]>({ kind: "library" }),
    [linkScope, setLinkScope] = useState<LinkOpportunitySnapshot["scope"]>({ kind: "library" }),
    [conceptIdentity, setConceptIdentity] = useState(""),
    [healthFilters, setHealthFilters] = useState<HealthFilters>({ status: "open" }),
    [gapFilters, setGapFilters] = useState<GapFilters>({ status: "open" }),
    [query, setQuery] = useState(""),
    [searchOpen, setSearchOpen] = useState(false),
    [commandInitialQuery, setCommandInitialQuery] = useState(""),
    [captureOpen, setCaptureOpen] = useState(false),
    [settingsOpen, setSettingsOpen] = useState(false),
    [operation, setOperation] = useState<Operation | null>(null),
    [loaded, setLoaded] = useState(false);
  const commandReturnFocus = useRef<HTMLElement | null>(null);
  const readUrl = useCallback(() => {
    const url = new URL(location.href);
    const id = url.searchParams.get("note");
    setActive(id && /^[0-9a-f-]{36}$/i.test(id) ? id : null);
    const view = url.searchParams.get("view");
    setProjectPath(url.searchParams.get("project") ?? "");
    setProjectSection(url.searchParams.get("section"));
    const evolutionKind = url.searchParams.get("evolutionKind");
    const evolutionTopic = url.searchParams.get("topic") ?? "";
    const evolutionPath = url.searchParams.get("evolutionPath") ?? "";
    const evolutionNote = url.searchParams.get("evolutionNote") ?? "";
    const knowledgeKind = url.searchParams.get("knowledgeKind");
    const knowledgePath = url.searchParams.get("knowledgePath") ?? "";
    const knowledgeNote = url.searchParams.get("knowledgeNote") ?? "";
    const overlapKind = url.searchParams.get("overlapKind");
    const overlapPath = url.searchParams.get("overlapPath") ?? "";
    const overlapNote = url.searchParams.get("overlapNote") ?? "";
    const linkKind = url.searchParams.get("linkKind");
    const linkPath = url.searchParams.get("linkPath") ?? "";
    const linkNote = url.searchParams.get("linkNote") ?? "";
    setConceptIdentity(url.searchParams.get("concept") ?? "");
    setHealthFilters({ status: (url.searchParams.get("healthStatus") as HealthFilters["status"]) ?? "open", category: (url.searchParams.get("healthCategory") as HealthFilters["category"]) ?? undefined, priority: (url.searchParams.get("healthPriority") as HealthFilters["priority"]) ?? undefined, project: url.searchParams.get("healthProject") ?? undefined, concept: url.searchParams.get("healthConcept") ?? undefined, noteId: url.searchParams.get("healthNote") ?? undefined });
    setGapFilters({ status: (url.searchParams.get("gapStatus") as GapFilters["status"]) ?? "open", kind: (url.searchParams.get("gapKind") as GapFilters["kind"]) ?? undefined, importance: (url.searchParams.get("gapImportance") as GapFilters["importance"]) ?? undefined, project: url.searchParams.get("gapProject") ?? undefined, concept: url.searchParams.get("gapConcept") ?? undefined, selected: url.searchParams.get("gapSelected") ?? undefined });
    if (evolutionKind === "note" && /^[0-9a-f-]{36}$/i.test(evolutionNote)) setEvolutionScope({ kind: "note", noteId: evolutionNote, topic: evolutionTopic || undefined });
    else if ((evolutionKind === "project" || evolutionKind === "folder") && evolutionPath) setEvolutionScope({ kind: evolutionKind, path: evolutionPath, topic: evolutionTopic || undefined });
    else setEvolutionScope({ kind: "topic", topic: evolutionTopic, path: evolutionPath || undefined });
    if (knowledgeKind === "note" && /^[0-9a-f-]{36}$/i.test(knowledgeNote)) setKnowledgeScope({ kind: "note", noteId: knowledgeNote });
    else if (knowledgeKind === "project" && knowledgePath) setKnowledgeScope({ kind: "project", path: knowledgePath });
    else setKnowledgeScope({ kind: "library" });
    if (overlapKind === "note" && /^[0-9a-f-]{36}$/i.test(overlapNote)) setOverlapScope({ kind: "note", noteId: overlapNote });
    else if (overlapKind === "project" && overlapPath) setOverlapScope({ kind: "project", path: overlapPath });
    else setOverlapScope({ kind: "library" });
    if (linkKind === "note" && /^[0-9a-f-]{36}$/i.test(linkNote)) setLinkScope({ kind: "note", noteId: linkNote });
    else if (linkKind === "project" && linkPath) setLinkScope({ kind: "project", path: linkPath });
    else setLinkScope({ kind: "library" });
    setNav(
      view === "inbox" ||
        view === "recent" ||
        view === "search" ||
        view === "recovery" ||
        view === "rediscover" ||
        view === "link-health" ||
        view === "favorites" ||
        view === "project-brain" ||
        view === "evolution" ||
        view === "knowledge-issues" ||
        view === "knowledge-overlap" ||
        view === "link-opportunities" ||
        view === "concepts" ||
        view === "library-health" ||
        view === "knowledge-gaps" ||
        isSmartView(view)
        ? view
        : "library",
    );
    setQuery(url.searchParams.get("q") ?? "");
    if (id && /^[0-9a-f-]{36}$/i.test(id))
      setTabs((t) =>
        t.some((x) => x.id === id)
          ? t
          : [...t, { id, title: "Opening…", path: "", dirty: false }],
      );
  }, []);
  useEffect(() => {
    try {
      const saved = JSON.parse(
        localStorage.getItem(`slate.tabs.${scope}`) ?? "[]",
      );
      if (Array.isArray(saved))
        setTabs(
          saved
            .filter(
              (t) => typeof t.id === "string" && typeof t.title === "string",
            )
            .slice(0, 30)
            .map((t) => ({ ...t, dirty: false })),
        );
    } catch {}
    readUrl();
    setLoaded(true);
    window.addEventListener("popstate", readUrl);
    return () => window.removeEventListener("popstate", readUrl);
  }, [scope, readUrl]);
  useEffect(() => {
    if (loaded)
      try {
        localStorage.setItem(
          `slate.tabs.${scope}`,
          JSON.stringify(tabs.map((t) => ({ ...t, dirty: false }))),
        );
      } catch {}
  }, [loaded, scope, tabs]);
  useEffect(() => {
    const handler = (e: BeforeUnloadEvent) => {
      if (tabs.some((t) => t.dirty)) {
        e.preventDefault();
        e.returnValue = "";
      }
    };
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, [tabs]);
  function urlChange(id: string | null, view: Destination, q = query) {
    const url = new URL(location.href);
    url.searchParams.set("view", view);
    if (id) url.searchParams.set("note", id);
    else url.searchParams.delete("note");
    if (q) url.searchParams.set("q", q);
    else url.searchParams.delete("q");
    if (view === "project-brain" && projectPath) url.searchParams.set("project", projectPath);
    else if (view !== "project-brain") url.searchParams.delete("section");
    if (view !== "evolution") {
      url.searchParams.delete("evolutionKind");
      url.searchParams.delete("evolutionPath");
      url.searchParams.delete("evolutionNote");
      url.searchParams.delete("topic");
    }
    if (view !== "knowledge-issues") {
      url.searchParams.delete("knowledgeKind");
      url.searchParams.delete("knowledgePath");
      url.searchParams.delete("knowledgeNote");
    }
    if (view !== "knowledge-overlap") {
      url.searchParams.delete("overlapKind");
      url.searchParams.delete("overlapPath");
      url.searchParams.delete("overlapNote");
    }
    if (view !== "link-opportunities") {
      url.searchParams.delete("linkKind"); url.searchParams.delete("linkPath"); url.searchParams.delete("linkNote");
    }
    if (view !== "concepts") url.searchParams.delete("concept");
    if (view !== "library-health") for (const key of ["healthStatus", "healthCategory", "healthPriority", "healthProject", "healthConcept", "healthNote"]) url.searchParams.delete(key);
    if (view !== "knowledge-gaps") for (const key of ["gapStatus", "gapKind", "gapImportance", "gapProject", "gapConcept", "gapSelected"]) url.searchParams.delete(key);
    history.pushState({}, "", url);
  }
  function open(id: string) {
    setTabs((t) =>
      t.some((x) => x.id === id)
        ? t
        : [...t, { id, title: "Opening…", path: "", dirty: false }],
    );
    setActive(id);
    urlChange(id, nav);
    setSearchOpen(false);
  }
  function close(id: string) {
    const tab = tabs.find((t) => t.id === id);
    if (
      tab?.dirty &&
      !confirm(
        "Close this tab? Its unsaved draft is kept in this browser and can be recovered when reopened.",
      )
    )
      return;
    const remaining = tabs.filter((t) => t.id !== id);
    setTabs((current) => current.filter((t) => t.id !== id));
    if (active === id) {
      const next = remaining.at(-1)?.id ?? null;
      setActive(next);
      urlChange(next, nav);
    }
  }
  const update = useCallback(
    (note: Pick<Note, "id" | "title" | "path">, dirty = false) =>
      setTabs((tabs) => {
        const old = tabs.find((tab) => tab.id === note.id);
        if (
          !old ||
          (old.title === note.title &&
            old.path === note.path &&
            old.dirty === dirty)
        )
          return tabs;
        return tabs.map((tab) =>
          tab.id === note.id ? { ...note, dirty } : tab,
        );
      }),
    [],
  );
  return (
    <Context.Provider
      value={{
        scope,
        tabs,
        active,
        nav,
        projectPath,
        projectSection,
        evolutionScope,
        knowledgeScope,
        overlapScope,
        linkScope,
        conceptIdentity,
        healthFilters,
        gapFilters,
        query,
        searchOpen,
        commandInitialQuery,
        captureOpen,
        settingsOpen,
        operation,
        open,
        close,
        closePath(path) {
          const matches = (tab: Tab) =>
            tab.path === path || tab.path.startsWith(path + "/");
          const remaining = tabs.filter((tab) => !matches(tab));
          setTabs((current) => current.filter((tab) => !matches(tab)));
          if (tabs.some((tab) => tab.id === active && matches(tab))) {
            const next = remaining.at(-1)?.id ?? null;
            setActive(next);
            urlChange(next, nav);
          }
        },
        update,
        navigate(view) {
          setNav(view);
          setActive(null);
          urlChange(
            null,
            view,
            new URL(location.href).searchParams.get("q") ?? "",
          );
        },
        openProjectBrain(path, section) {
          const normalized = path.replace(/\\/g, "/").replace(/^\/+|\/+$/g, "");
          setProjectPath(normalized);
          setProjectSection(section ?? null);
          setNav("project-brain");
          setActive(null);
          const url = new URL(location.href);
          url.searchParams.set("view", "project-brain");
          url.searchParams.set("project", normalized);
          url.searchParams.delete("note");
          if (section) url.searchParams.set("section", section);
          else url.searchParams.delete("section");
          history.pushState({}, "", url);
        },
        openEvolution(scope) {
          setEvolutionScope(scope);
          setNav("evolution");
          setActive(null);
          const url = new URL(location.href);
          url.searchParams.set("view", "evolution");
          url.searchParams.set("evolutionKind", scope.kind);
          url.searchParams.delete("note");
          url.searchParams.delete("project");
          url.searchParams.delete("section");
          if ("topic" in scope && scope.topic) url.searchParams.set("topic", scope.topic);
          else url.searchParams.delete("topic");
          if ("path" in scope && scope.path) url.searchParams.set("evolutionPath", scope.path);
          else url.searchParams.delete("evolutionPath");
          if (scope.kind === "note") url.searchParams.set("evolutionNote", scope.noteId);
          else url.searchParams.delete("evolutionNote");
          history.pushState({}, "", url);
        },
        openKnowledgeIssues(scope = { kind: "library" }) {
          setKnowledgeScope(scope);
          setNav("knowledge-issues");
          setActive(null);
          const url = new URL(location.href);
          url.searchParams.set("view", "knowledge-issues");
          url.searchParams.set("knowledgeKind", scope.kind);
          url.searchParams.delete("note");
          if (scope.kind === "project") url.searchParams.set("knowledgePath", scope.path ?? "");
          else url.searchParams.delete("knowledgePath");
          if (scope.kind === "note") url.searchParams.set("knowledgeNote", scope.noteId ?? "");
          else url.searchParams.delete("knowledgeNote");
          history.pushState({}, "", url);
        },
        openKnowledgeOverlap(scope = { kind: "library" }) {
          setOverlapScope(scope); setNav("knowledge-overlap"); setActive(null);
          const url = new URL(location.href); url.searchParams.set("view", "knowledge-overlap"); url.searchParams.set("overlapKind", scope.kind); url.searchParams.delete("note");
          if (scope.kind === "project") url.searchParams.set("overlapPath", scope.path ?? ""); else url.searchParams.delete("overlapPath");
          if (scope.kind === "note") url.searchParams.set("overlapNote", scope.noteId ?? ""); else url.searchParams.delete("overlapNote");
          history.pushState({}, "", url);
        },
        openLinkOpportunities(scope = { kind: "library" }) {
          setLinkScope(scope); setNav("link-opportunities"); setActive(null);
          const url = new URL(location.href); url.searchParams.set("view", "link-opportunities"); url.searchParams.set("linkKind", scope.kind); url.searchParams.delete("note");
          if (scope.kind === "project") url.searchParams.set("linkPath", scope.path ?? ""); else url.searchParams.delete("linkPath");
          if (scope.kind === "note") url.searchParams.set("linkNote", scope.noteId ?? ""); else url.searchParams.delete("linkNote");
          history.pushState({}, "", url);
        },
        openConcept(identity = "") {
          setConceptIdentity(identity.trim()); setNav("concepts"); setActive(null);
          const url = new URL(location.href); url.searchParams.set("view", "concepts"); url.searchParams.delete("note");
          if (identity.trim()) url.searchParams.set("concept", identity.trim()); else url.searchParams.delete("concept");
          history.pushState({}, "", url);
        },
        openLibraryHealth(filters = { status: "open" }) {
          const next = { status: "open" as const, ...filters }; setHealthFilters(next); setNav("library-health"); setActive(null);
          const url = new URL(location.href); url.searchParams.set("view", "library-health"); url.searchParams.delete("note");
          const values: Array<[string, string | undefined]> = [["healthStatus", next.status], ["healthCategory", next.category], ["healthPriority", next.priority], ["healthProject", next.project], ["healthConcept", next.concept], ["healthNote", next.noteId]];
          for (const [key, value] of values) if (value) url.searchParams.set(key, value); else url.searchParams.delete(key);
          history.pushState({}, "", url);
        },
        openKnowledgeGaps(filters = { status: "open" }) {
          const next = { status: "open" as const, ...filters }; setGapFilters(next); setNav("knowledge-gaps"); setActive(null);
          const url = new URL(location.href); url.searchParams.set("view", "knowledge-gaps"); url.searchParams.delete("note");
          const values: Array<[string, string | undefined]> = [["gapStatus", next.status], ["gapKind", next.kind], ["gapImportance", next.importance], ["gapProject", next.project], ["gapConcept", next.concept], ["gapSelected", next.selected]];
          for (const [key, value] of values) if (value) url.searchParams.set(key, value); else url.searchParams.delete(key);
          history.pushState({}, "", url);
        },
        setQuery(q) {
          setQuery(q);
          const url = new URL(location.href);
          if (q) url.searchParams.set("q", q);
          else url.searchParams.delete("q");
          history.replaceState({}, "", url);
        },
        setSearchOpen(value) {
          setSearchOpen(value);
          if (!value) {
            setCommandInitialQuery("");
            const target = commandReturnFocus.current;
            if (target?.isConnected) requestAnimationFrame(() => target.focus());
          }
        },
        openCommandCenter(initial = "") {
          commandReturnFocus.current = document.activeElement as HTMLElement | null;
          setCommandInitialQuery(initial);
          setSearchOpen(true);
        },
        setCaptureOpen,
        setSettingsOpen,
        setOperation,
      }}
    >
      {children}
    </Context.Provider>
  );
}
export function useWorkspace() {
  const v = useContext(Context);
  if (!v) throw new Error("Workspace missing");
  return v;
}
