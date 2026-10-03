import { describe, expect, it } from "vitest";
import { searchFilterChips, setSearchFilter } from "@/components/search/search";

describe("visual search filters", () => {
  it("adds and replaces one direct-query filter without hiding free text", () => {
    expect(setSearchFilter("mqtt type:link", "type", "question")).toBe(
      "mqtt type:question",
    );
    expect(setSearchFilter("exact phrase", "path", "Projects/Slate docs")).toBe(
      'exact phrase path:"Projects/Slate docs"',
    );
  });

  it("removes a filter when Any is selected", () => {
    expect(setSearchFilter("status:open postgres", "status", "")).toBe(
      "postgres",
    );
  });
});

it("extracts direct syntax into removable filter chips", () => {
  expect(searchFilterChips('vector path:"Research Notes" tag:search')).toEqual([
    { key: "path", value: "Research Notes" },
    { key: "tag", value: "search" },
  ]);
});
