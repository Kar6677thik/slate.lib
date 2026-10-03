import { beforeEach, describe, expect, it, vi } from "vitest";
import { Search } from "lucide-react";
import { fuzzyScore, rankWithRecency } from "@/features/commands/ranking";
import {
  availableCommands,
  collectCommandSources,
  commandResults,
} from "@/features/commands/registry";
import type { CommandDefinition } from "@/features/commands/types";
import {
  clearCommandHistory,
  readCommandHistory,
  recordCommandUse,
} from "@/lib/storage/command-history";

function command(
  id: string,
  title: string,
  overrides: Partial<CommandDefinition> = {},
): CommandDefinition {
  return {
    id,
    title,
    icon: Search,
    keywords: [],
    group: "Workspace",
    execute: () => {},
    ...overrides,
  };
}

describe("Command Center registry", () => {
  beforeEach(() => localStorage.clear());

  it("scores exact, prefix, word-prefix, contains, and subsequence matches predictably", () => {
    expect(fuzzyScore("save note", ["Save note"])).toBe(120);
    expect(fuzzyScore("save", ["Save note"])).toBeGreaterThan(90);
    expect(fuzzyScore("note", ["Save note"])).toBeGreaterThan(70);
    expect(fuzzyScore("ave", ["Save note"])).toBeGreaterThan(50);
    expect(fuzzyScore("svnt", ["Save note"])).toBeGreaterThan(0);
    expect(fuzzyScore("xyz", ["Save note"])).toBe(0);
  });

  it("adds a small deterministic recency bonus without replacing relevance", () => {
    expect(rankWithRecency(50, 0)).toBe(62);
    expect(rankWithRecency(100, -1)).toBe(100);
    expect(rankWithRecency(50, 20)).toBeLessThan(rankWithRecency(50, 0));
  });

  it("keeps at most 25 recent commands in most-recent-first order", () => {
    for (let index = 0; index < 30; index++) recordCommandUse("scope", `command-${index}`);
    const history = readCommandHistory("scope");
    expect(history).toHaveLength(25);
    expect(history[0].id).toBe("command-29");
    expect(history.at(-1)?.id).toBe("command-5");
  });

  it("deduplicates repeated command use", () => {
    recordCommandUse("scope", "save");
    recordCommandUse("scope", "search");
    recordCommandUse("scope", "save");
    expect(readCommandHistory("scope").map((item) => item.id)).toEqual(["save", "search"]);
  });

  it("clears command history independently", () => {
    recordCommandUse("scope", "save");
    clearCommandHistory("scope");
    expect(readCommandHistory("scope")).toEqual([]);
  });

  it("hides context commands when their availability predicate is false", () => {
    const commands = [
      command("save", "Save", { when: () => false }),
      command("search", "Search"),
      command("desktop-only", "Desktop", { mobileVisible: false }),
    ];
    expect(availableCommands(commands).map((item) => item.id)).toEqual(["search"]);
  });

  it("matches command keywords as well as titles", () => {
    const results = commandResults(
      [command("details.graph", "Open note graph", { keywords: ["connections"] })],
      "connections",
      [],
    );
    expect(results[0].commandId).toBe("details.graph");
  });

  it("boosts a recently used command in an otherwise equal result set", () => {
    const results = commandResults(
      [command("one", "Open one"), command("two", "Open two")],
      "open",
      ["two"],
    ).sort((a, b) => b.score - a.score);
    expect(results[0].commandId).toBe("two");
  });

  it("executes the existing safeguard delegate for dangerous actions", async () => {
    const guarded = vi.fn();
    const result = commandResults(
      [command("delete", "Delete note", { dangerous: true, execute: guarded })],
      "delete",
      [],
    )[0];
    await result.execute();
    expect(result.dangerous).toBe(true);
    expect(guarded).toHaveBeenCalledOnce();
  });

  it("collects static and dynamic providers through one source contract", () => {
    const results = collectCommandSources(
      [
        {
          id: "tabs",
          collect: () => [{
            key: "tab:1",
            title: "Open tab",
            icon: Search,
            group: "Open tabs",
            score: 1,
            execute: () => {},
          }],
        },
        {
          id: "favorites",
          collect: () => [{
            key: "favorite:1",
            title: "Favorite note",
            icon: Search,
            group: "Favorites",
            score: 1,
            execute: () => {},
          }],
        },
      ],
      "",
    );
    expect(results.map((result) => result.group)).toEqual(["Open tabs", "Favorites"]);
  });
});
