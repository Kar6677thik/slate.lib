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
    setNav(
      view === "inbox" ||
        view === "recent" ||
        view === "search" ||
        view === "recovery" ||
        view === "rediscover" ||
        view === "link-health" ||
        view === "favorites" ||
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
