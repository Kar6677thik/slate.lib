import { act, renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import {
  readWorkspacePreferences,
  useWorkspacePreferences,
} from "@/lib/storage/workspace-preferences";

describe("library-scoped workspace preferences", () => {
  it("keeps favorites, pins and saved searches within one library scope", () => {
    const { result } = renderHook(() => useWorkspacePreferences("server:one"));
    act(() => {
      result.current.toggleFavorite({
        id: "00000000-0000-4000-8000-000000000001",
        title: "Architecture",
        path: "projects/architecture.md",
      });
      result.current.togglePin({ path: "projects", label: "Projects" });
      result.current.saveSearch({
        id: "00000000-0000-4000-8000-000000000002",
        name: "Open questions",
        query: "type:question status:open",
      });
    });
    expect(readWorkspacePreferences("server:one")).toMatchObject({
      favorites: [{ title: "Architecture" }],
      pins: [{ path: "projects" }],
      searches: [{ query: "type:question status:open" }],
    });
    expect(readWorkspacePreferences("server:two").favorites).toEqual([]);
  });

  it("toggles shortcuts without duplicating them", () => {
    const { result } = renderHook(() => useWorkspacePreferences("toggle"));
    const favorite = {
      id: "00000000-0000-4000-8000-000000000003",
      title: "One",
      path: "one.md",
    };
    act(() => result.current.toggleFavorite(favorite));
    act(() => result.current.toggleFavorite(favorite));
    expect(readWorkspacePreferences("toggle").favorites).toEqual([]);
  });
});
