"use client";
import { useCallback, useSyncExternalStore } from "react";

export interface FavoriteNote {
  id: string;
  title: string;
  path: string;
}
export interface FolderPin {
  path: string;
  label: string;
}
export interface SavedSearch {
  id: string;
  name: string;
  query: string;
}
export interface WorkspacePreferences {
  schemaVersion: 1;
  favorites: FavoriteNote[];
  pins: FolderPin[];
  projects: FolderPin[];
  searches: SavedSearch[];
}

const empty: WorkspacePreferences = {
  schemaVersion: 1,
  favorites: [],
  pins: [],
  projects: [],
  searches: [],
};
const cache = new Map<string, WorkspacePreferences>();
const cachedStorage = new Map<string, string | null>();
const eventName = "slate-workspace-preferences";
const key = (scope: string) => `slate.workspace-preferences.${scope}`;

function validString(value: unknown, maximum: number) {
  return typeof value === "string" && value.length > 0 && value.length <= maximum;
}
function parse(value: string | null): WorkspacePreferences {
  if (!value) return empty;
  try {
    const raw = JSON.parse(value) as Partial<WorkspacePreferences>;
    if (raw.schemaVersion !== 1) return empty;
    return {
      schemaVersion: 1,
      favorites: Array.isArray(raw.favorites)
        ? raw.favorites
            .filter(
              (item) =>
                validString(item?.id, 36) &&
                validString(item?.title, 200) &&
                validString(item?.path, 500),
            )
            .slice(0, 1000)
        : [],
      pins: Array.isArray(raw.pins)
        ? raw.pins
            .filter(
              (item) =>
                validString(item?.path, 500) && validString(item?.label, 200),
            )
            .slice(0, 200)
        : [],
      projects: Array.isArray(raw.projects)
        ? raw.projects
            .filter((item) => validString(item?.path, 500) && validString(item?.label, 200))
            .slice(0, 100)
        : [],
      searches: Array.isArray(raw.searches)
        ? raw.searches
            .filter(
              (item) =>
                validString(item?.id, 36) &&
                validString(item?.name, 80) &&
                validString(item?.query, 512),
            )
            .slice(0, 100)
        : [],
    };
  } catch {
    return empty;
  }
}
export function readWorkspacePreferences(scope: string) {
  if (typeof localStorage === "undefined") return empty;
  const saved = localStorage.getItem(key(scope));
  const current = cache.get(scope);
  if (current && cachedStorage.get(scope) === saved) return current;
  const next = parse(saved);
  cache.set(scope, next);
  cachedStorage.set(scope, saved);
  return next;
}
function write(scope: string, change: (current: WorkspacePreferences) => WorkspacePreferences) {
  const next = change(readWorkspacePreferences(scope));
  const saved = JSON.stringify(next);
  localStorage.setItem(key(scope), saved);
  cache.set(scope, next);
  cachedStorage.set(scope, saved);
  window.dispatchEvent(new CustomEvent(eventName, { detail: scope }));
}
function subscribe(scope: string, callback: () => void) {
  const onChange = (event: Event) => {
    if (!(event instanceof CustomEvent) || event.detail === scope) callback();
  };
  window.addEventListener(eventName, onChange);
  window.addEventListener("storage", callback);
  return () => {
    window.removeEventListener(eventName, onChange);
    window.removeEventListener("storage", callback);
  };
}
export function useWorkspacePreferences(scope: string) {
  const preferences = useSyncExternalStore(
    useCallback((callback) => subscribe(scope, callback), [scope]),
    useCallback(() => readWorkspacePreferences(scope), [scope]),
    () => empty,
  );
  return {
    preferences,
    toggleFavorite(note: FavoriteNote) {
      write(scope, (current) => ({
        ...current,
        favorites: current.favorites.some((item) => item.id === note.id)
          ? current.favorites.filter((item) => item.id !== note.id)
          : [...current.favorites, note],
      }));
    },
    togglePin(pin: FolderPin) {
      write(scope, (current) => ({
        ...current,
        pins: current.pins.some(
          (item) => item.path.toLowerCase() === pin.path.toLowerCase(),
        )
          ? current.pins.filter(
              (item) => item.path.toLowerCase() !== pin.path.toLowerCase(),
            )
          : [...current.pins, pin],
      }));
    },
    toggleProject(project: FolderPin) {
      write(scope, (current) => ({
        ...current,
        projects: current.projects.some((item) => item.path.toLowerCase() === project.path.toLowerCase())
          ? current.projects.filter((item) => item.path.toLowerCase() !== project.path.toLowerCase())
          : [...current.projects, project],
      }));
    },
    saveSearch(search: SavedSearch) {
      write(scope, (current) => ({
        ...current,
        searches: [
          ...current.searches.filter((item) => item.id !== search.id),
          search,
        ],
      }));
    },
    deleteSearch(id: string) {
      write(scope, (current) => ({
        ...current,
        searches: current.searches.filter((item) => item.id !== id),
      }));
    },
  };
}
