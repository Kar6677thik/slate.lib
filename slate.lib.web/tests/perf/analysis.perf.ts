/*
 * Local CPU reproduction for whole-library analyses. Not part of `pnpm test`; run with
 *   pnpm vitest run --config tests/perf/vitest.perf.config.ts --silent=false
 */
import { describe, it } from "vitest";
import { library, run } from "./library";

describe("analysis CPU", () => {
  it("typical library", () => run("typical", library(240)), 600_000);
  it("library with large notes", () => run("large notes", library(240, { large: 6 })), 600_000);
});
