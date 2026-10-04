# Verification

## Milestone 9

Smart-link verification covers exact and normalized titles, aliases, ambiguous and project-local targets, heading links, existing-link suppression, prose masking, generic-term suppression, graph triangles, project and claim relationships, duplicate/overlap behavior, Knowledge Issue warnings, better targets, safe and stale insertion, review fingerprints, bounds, and generation-disabled behavior.

Playwright covers Command Center entry, library review, source/target navigation, editor insertion and unsaved state, normal Save, stale-source rejection, persisted dismissal, Project Brain, Inbox, graph suggestions, Ask Slate, desktop/mobile review, mobile insertion, and axe checks. Normal CI never calls a real generation provider.

## Milestone 4 release gate

Run `pnpm lint`, `pnpm typecheck`, `pnpm test`, `pnpm build`, and the relevant Playwright project. Deterministic coverage includes Markdown/frontmatter chunking, code bounds, rename hash reuse, provider vector stability, metadata filter parsing, reciprocal-rank fusion, exact-title priority, bounded semantic relationship evidence, Ask context/source budgets, citation validation, prompt-injection boundaries, disabled/fake/failing generation providers, streaming cancellation, timeouts, sanitized output, browser-local conversation and review limits, every scope, insertion revision/range protection, and insufficient evidence.

In staging, verify lexical-only operation, disabled-provider fallback, index status, rebuild confirmation, all three Search modes, Command Center free-text retrieval, and unchanged `>` command behavior. With a real private provider, also verify streaming and cancellation, strict/general answer quality, citation correctness, provider usage reporting, that a save succeeds during provider outage, delete removes derived chunks, sync queues reconciliation, and browser responses and logs contain no API key, device token, full note body, raw prompt, generated answer, or raw embedding.

Final local verification: 3 October 2026. All implementation and generated artifacts are inside `slate.lib.web`.

| Check | Result |
| --- | --- |
| ESLint | Passed |
| Strict TypeScript | Passed |
| Vitest / Testing Library | 64 tests passed across 14 files, including deterministic chunking, vector reuse, filters, hybrid ranking, Ask context budgets, grounding, citation validation, provider failure/cancellation, output sanitization, origin/server validation, and oversized-request safeguards |
| Next production build | Passed; standalone server starts successfully |
| Playwright Chromium | 56 passed, 1 optional real-server integration test skipped, including 11 hybrid-search and 12 Ask Slate scenarios |
| Axe accessibility | No violations in tested connection, light/dark reader, settings, mobile editor, Command Center, and desktop/mobile Ask Slate states |
| Responsive layouts | Passed at 390×844, 412×915, 768×1024, 1366×768, 1440×900, 1920×1080 |
| PWA | Registration, manifest, static-only cache and offline fallback passed |
| Docker container | Linux image built; non-root/read-only runtime and HTTP checks passed; browser suite: 22 passed, 1 optional integration skipped |
| k3s | Template provided; not applied or cluster-validated |

## Browser scenarios

- Connect, lazily expand folders, open UUID tabs, read/edit/save and search saved content.
- Preserve local text on revision conflict, revoked token and failed background refresh.
- Reload and explicitly recover an unsaved IndexedDB draft.
- Capture into Inbox; retry with an identical UUID, timestamp and body after failure.
- Create folders/notes; move, rename, duplicate and explicitly delete fixture items.
- Upload attachments, paste image bytes, insert canonical asset links and download controls.
- Navigate backlinks, inspect read-only historical versions and invoke sync.
- Use server-backed smart views, rediscovery reasons, Daily Note, related notes and the bounded current-note graph.
- Multi-select folders, inspect the server-generated bulk plan and apply one idempotent operation.
- Preview wiki-link conversion as source text and explicitly apply it with revision protection.
- Compare history with the current note, restore a revision, restore as new and recover deleted notes.
- Save library-scoped favorites, pinned folders and named searches in versioned browser preferences.
- Persist theme and recent activity; operate mobile drawers, capture, search and editor controls.
- Block raw HTML/script execution and unsafe links; render math and diagrams on demand.
- Open the Universal Command Center from keyboard and global search, use blended and command-only modes, search notes, execute contextual save/details/file actions, preserve editor `Ctrl/Cmd+K`, navigate/close tabs through shared handlers, and use the mobile bottom sheet.
- Open Ask Slate from the Command Center and workspace chrome; use library, note, folder, deterministic project and selected-note scopes; pass selected editor text as explicit context; stream answers; validate and open citations; copy source links; ask follow-ups with repeated retrieval; clear local history; distinguish general context; refuse insufficient evidence; degrade when generation is unavailable; stop an in-flight answer; contain adversarial document instructions; and use an accessible full-screen mobile experience.
- Open Project Brain from folder actions and explicit project roots; verify deterministic overview/current/history/decisions/questions/architecture/ideas/experiments/failures/risks/timeline; use project search and Ask scope; navigate the project graph list and outside relations; open and return from sources; generate and refresh a cited Resume Project pack; verify generation-disabled desktop/mobile behavior and accessibility.

All mutation scenarios use local, stateful mocked API fixtures. They do not contact or alter the home server. The optional real-server test was skipped because no development credentials were supplied.

## Visual review

Inspected the generated desktop light/dark reader, mobile reader and mobile editor screenshots. Screenshots contain fixture notes, not private library data. Outputs are ignored by Git:

- `output/playwright/workspace-1440.png`
- `output/playwright/workspace-dark.png`
- `output/playwright/workspace-390.png`
- `output/playwright/mobile-editor.png`
- `output/playwright/settings-dark.png`

The interface uses neutral surfaces, one restrained accent, compact document tools, predictable hover/focus states and minimal motion. No gradients, glass effects, decorative feature cards or scroll-triggered effects.

## Performance boundaries

Folder reads are lazy and paged; search is debounced, cancellable and server-ranked. Recent activity is capped at 50 notes. CodeMirror loads on first editing, while math and diagram modules load only when needed. Images load near the viewport; blob URLs are revoked. Drafts reuse an IndexedDB connection and serialize writes. Tab state does not rebuild the workspace on every keystroke once the dirty state is unchanged.

There is no synthetic Lighthouse score or claim of a measured large-library latency target. Production network latency, real library scale, physical Android keyboard/IME behavior, Safari/Firefox and OS-level PWA installation still require validation on the actual deployment.

## Deliberate limits

- No full offline library, automatic replay UI, three-way merge UI, OCR, automatic summaries, AI writing/rewrite, contradiction or duplicate detection, knowledge-gap analysis, autonomous actions, note mutation through AI, or collaboration in this milestone. Ask Slate is the only generation feature and remains read-only.
- Project Brain generation is also read-only. It does not create project summaries, decision logs, tasks, status notes, or recommendations, and its graph is a bounded explicit-link view rather than a whole-library visualization.
- The destination picker filters already-browsed folders; browse deeper or enter the exact path for an unvisited destination. It never scans the complete library just to build a picker.
- External images are not loaded; uploaded assets use authenticated requests.
- Browser-reserved shortcuts can override application shortcuts.
- Command usage history, favorites, pins, recent notes, and saved searches are scoped to this browser and library. They do not sync between browsers.
- Browser drafts are a recovery aid, not a backup. Persistent credentials are opt-in browser storage, not an encrypted vault.
- Deployment requires an approved backend URL reachable from the web container, a real image digest and private HTTPS routing. Existing backend/native routes must remain intact.

No production deployment, Git push, release publication or commit was performed.

## Docker verification follow-up

After Docker Desktop was started, the image built successfully from this folder with the frozen dependency lockfile. Local image tag: `slate-lib-web:verify-2de1a8f41fcf`.

The verification container ran with UID/GID 1000, a read-only root filesystem, all capabilities dropped, no-new-privileges, a 512 MiB memory limit, one CPU, a 128-process limit, and a 32 MiB `/tmp` tmpfs. Its port was bound to loopback only. No host directories, library data, production URLs or real tokens were mounted or supplied.

The app, manifest, service worker, icon and offline page returned HTTP 200. Real proxy requests rejected an unapproved server with 403 and a missing bearer token with 401. That earlier container baseline passed its complete Chromium suite, including PWA and accessibility checks. The current Milestone 3 changes were verified through the local production build and the 44-scenario Chromium suite; the container image was not rebuilt in this milestone.

The temporary verification container was stopped and retained, along with its image. Browser reports are in `output/docker/playwright-report`. Use `playwright.container.config.ts` with `SLATE_CONTAINER_URL` to repeat browser checks against a running local container. Real-backend connectivity, a real generation-provider quality/usage check, the Milestone 4 container image, and k3s deployment remain separate checks.
## Milestone 6: Evolution of Thought

Verification covers trusted-date handling, Markdown normalization, typo/cosmetic suppression, answered-question transitions, architecture and decision changes, cautious possible shifts, documented rationale, bounded canonical/history retrieval, prompt-injection boundaries, citation validation, cache identity, and generation-disabled operation.

Playwright covers Command Center, active-note, Project Brain, and search-result entry points; event filters; answered questions; superseded ideas; before/after evidence; historical/current navigation; synthesis headings and citations; Ask follow-up; no-AI behavior; responsive layout; and desktop/mobile accessibility. Run `pnpm check` followed by `pnpm test:e2e`.

## Milestone 7: contradictions and stale knowledge

Unit coverage verifies structured extraction, architecture/decision/requirement/version/status/question claims, code and quote suppression, direct reversal, value/version/architecture/decision conflicts, equal-value and different-service suppression, explicit supersession, possible answered questions, age-only safety, current/history separation, review persistence, fingerprint reopening, bounded candidate generation, model ambiguity classification, cache reuse, disabled and failed providers, invalid structured output, and prompt-injection containment.

Playwright fixtures cover Command Center entry, current contradiction, Source A and Source B, side-by-side comparison, persistent dismiss, resolve, version conflict, likely superseded material, possible answered questions, Evolution, Ask Slate, Project Brain, provider-disabled deterministic operation, mobile list/comparison, and desktop/mobile axe checks. Normal CI performs no real provider calls.

Real-provider verification remains an operator check: confirm any configured classifier returns schema-valid bounded results, fails closed on invalid output, and receives only the two claim excerpts plus minimal context.

## Milestone 8: duplicate and overlap intelligence

Unit coverage verifies whitespace and frontmatter normalization, identity/timestamp removal, preservation of meaningful values/links/code, exact copies, metadata-only differences, near duplicates, related-but-distinct suppression, section overlap, shared/unique sections, chronology-supported absorption, age-only safety, fragmentation, intent-based false-positive suppression, stable review fingerprints, bounded candidates, structured optional classification, cache reuse, disabled/failed providers, invalid output, and prompt-injection containment.

Playwright fixtures cover Command Center entry, exact/near/partial findings, A/B navigation, shared and unique tabs, persistent Keep Separate, browser-local merge draft, absorbed-note Evolution, Inbox overlap, Ask Slate, generation-disabled operation, Project Brain, mobile list/comparison, and desktop/mobile axe. Real-provider validation remains separate and must confirm bounded evidence, schema-valid output, conservative related-versus-duplicate behavior, and failure fallback.
## Entity and Concept Pages

- Unit coverage verifies title, alias, heading, tag, claim-subject and technical-phrase extraction; generic, code, log, URL and ID suppression; punctuation and case normalization; Postgres/PostgreSQL equivalence; `.NET`/`ASP.NET Core` separation; memberships; canonical project ancestry; authored-link and co-reference relationships; page sections; review state; degraded operation; security; and hard bounds.
- Playwright covers command navigation, concept search and pages, key notes, projects, related concepts, decisions, questions, Evolution, Knowledge Issues, Overlap, Link Opportunities, Ask Slate, Command Center results, note details, Project Brain, identity corrections, generation-disabled operation, responsive mobile navigation, and desktop/mobile axe scans.
- No paid generation or embedding provider is called by deterministic fixtures.

## Library Health and Maintenance Inbox

- Unit coverage verifies source aggregation, priority and evidence rules, specialist ownership, stable review state, project/note/concept/category/search filters, pagination and bounds, intentional orphan exclusions, empty-note handling, missing and unused assets, invalid and duplicate metadata, concept association, persistent intelligence failures, partial-source behavior, and non-mutating fingerprints.
- The 25-scenario Playwright suite covers Command Center entry, priority ordering, specialist routing for links/consistency/overlap/relationships, answered questions, orphans, assets, intelligence failures, project/concept/category filters, Review Next, health-only dismissal, underlying resolution, Project Brain and current-note summaries, generation-disabled and partial-source operation, mobile list/detail behavior, and desktop/mobile axe scans.
- Fixtures are deterministic and call no paid embedding or generation provider. Run `pnpm lint`, `pnpm typecheck`, `pnpm test`, `pnpm build`, then `pnpm exec playwright test e2e/library-health.spec.ts`.

## Knowledge Gap Finder

- Unit coverage verifies thin and fragmented coverage, dedicated and collective strong-source suppression, missing overview and architecture rules, decision rationale, operational recovery, recurring-question clustering and answered-topic suppression, bridge evidence, Daily/Inbox/template/code/log/quote false-positive controls, framework incidence, review fingerprints, local grounded drafts, degraded operation, injection isolation, paging, and hard bounds.
- The 25-scenario Playwright suite covers Command Center entry, each core finding kind, evidence and canonical-source navigation, Concept Page and Project Brain summaries, Library Health routing, Ask Slate, browser-local drafts with no automatic save, persistent Dismiss and Keep Fragmented states, generation/semantic/PostgreSQL degradation, mobile list/detail/draft behavior, and desktop/mobile axe scans.
- Deterministic fixtures make no real provider calls. Run `pnpm lint`, `pnpm typecheck`, `pnpm test`, `pnpm build`, then `pnpm exec playwright test e2e/knowledge-gaps.spec.ts`. Real-library scale, optional provider classification quality, and production PostgreSQL behavior remain separate live checks.
## Milestone 13: Inbox intelligence

Automated coverage verifies deterministic classification (including ambiguous, quoted, code, and log false positives), project ambiguity and suppression, related-note and Concept evidence, Overlap reuse, existing-folder suggestions, Knowledge Gap relationships, action previews, source preservation, fingerprints, queue ordering, bounds, degraded operation, prompt-injection inertness, keyboard review, browser-draft append, stale-target rejection, mobile Inbox Zero, and desktop/mobile accessibility.

Run:

```text
pnpm lint
pnpm typecheck
pnpm test
pnpm build
pnpm test:e2e -- e2e/inbox-triage.spec.ts
git diff --check
```

Ordinary CI uses deterministic fixtures and makes no real provider calls. Live-provider verification is optional and limited to explicitly enabled classification experiments; it is not required for Inbox triage correctness.
