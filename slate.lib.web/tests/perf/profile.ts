/*
 * CPU profile of the large-note library. Bundle and run with:
 *   npx rolldown tests/perf/profile.ts --format esm --platform node -o .perf/profile.mjs
 *   node --cpu-prof --cpu-prof-dir=.perf .perf/profile.mjs
 */
import { library, run } from "./library";

run("large notes", library(240, { large: 6 }));
