import type { Page, Route } from "@playwright/test";
import type { Note, Asset } from "../src/lib/api/contracts";
import { smartLinkHash } from "../src/lib/intelligence/smart-links";
import type { LibraryHealthItem } from "../src/lib/intelligence/health";
import type { KnowledgeGap } from "../src/lib/intelligence/knowledge-gaps";
export const first = "00000000-0000-4000-8000-000000000001",
  second = "00000000-0000-4000-8000-000000000002";
export const libraryId = "10000000-0000-4000-8000-000000000001";
export const scope = "https://fixture.slate.test:" + libraryId;
export async function mockSlate(page: Page, options: { generationEnabled?: boolean; healthPartialFailure?: boolean; semanticEnabled?: boolean; postgresAvailable?: boolean } = {}) {
  const notes = new Map<string, Note>([
    [
      first,
      {
        id: first,
        path: "Projects/Library architecture.md",
        title: "Library architecture",
        revision: '"r1"',
        markdown: `---\nid: ${first}\n---\n\n# Library architecture\n\nA working reference for how we store, connect, and maintain the library.\n\n## Principles\n\nKeep the original files readable. Use stable identities for links. Make every change easy to review.\n\n- Markdown is the source of truth\n- Folders describe where a note belongs\n- Links describe how ideas connect\n\n> [!NOTE]\n> Start with a small structure. Add folders when the work calls for them.\n\n## Storage decisions\n\n| Content | Location |\n| --- | --- |\n| Notes | Markdown files |\n| Attachments | Asset storage |\n| History | Git repository |\n\n## Next steps\n\n- [x] Document the storage model\n- [ ] Review [[Reading list]]\n\n\`\`\`typescript\nconst library = { source: "markdown", private: true };\n\`\`\`\n`,
      },
    ],
    [
      second,
      {
        id: second,
        path: "Research/Reading list.md",
        title: "Reading list",
        revision: '"r1"',
        markdown: `---\nid: ${second}\n---\n\n# Reading list\n\nNotes on distributed systems and reliable storage.\n\nSee [[Library architecture]].\n`,
      },
    ],
  ]);
  const folders = new Set(["Projects", "Research", "inbox"]);
  const assets = new Map<string, { meta: Asset; bytes: Buffer }>();
  const bulkPlans = new Map<
    string,
    {
      operation: string;
      items: { sourcePath: string; destinationPath: string | null; isDirectory: boolean }[];
      fingerprint: string;
    }
  >();
  const requests: { path: string; method: string; body: unknown }[] = [];
  const askRequests: { question: string; policy: "strict" | "general"; scope: { kind: string; noteId?: string; noteIds?: string[]; path?: string; selectedText?: string } }[] = [];
  const projectRequests: { action: string; path: string; kind?: string; refresh?: boolean }[] = [];
  const evolutionRequests: Array<{ action: string; scope: { kind: string; topic?: string; path?: string; noteId?: string; noteIds?: string[] }; refresh?: boolean }> = [];
  let conflict = false;
  let assetCounter = 0;
  function getEntry(n: Note) {
    return {
      name: n.path.split("/").at(-1),
      path: n.path,
      isDirectory: false,
      id: n.id,
      title: n.title,
    };
  }
  await page.route("**/api/intelligence/**", async (route: Route) => {
    const req = route.request();
    const endpoint = new URL(req.url()).pathname.split("/").at(-1);
    const respond = (data: unknown, status = 200) => route.fulfill({ status, contentType: "application/json", body: JSON.stringify(data) });
    if (endpoint === "status") return respond({ enabled: options.semanticEnabled !== false, state: "ready", provider: "fixture", model: "fixture-v1", dimensions: 32, noteCount: notes.size, totalNotes: notes.size, chunkCount: notes.size * 3, pendingJobs: 0, failedJobs: 0, embeddedThisRun: 0, reusedThisRun: notes.size * 3, failedChunksThisRun: 0, lastIndexedAt: "2026-10-03T10:00:00Z", lastError: null, askEnabled: true, generationProvider: "deterministic-test", generationModel: "grounded-v1", askDefaultPolicy: "strict", askRequestsThisRun: 0, askRetrievedChunksThisRun: 0, askActiveRequests: 0, askMaxConcurrent: 2 });
    if (endpoint === "rebuild") return respond({ state: "pending" }, 202);
    if (endpoint === "events") return route.fulfill({ status: 202 });
    if (endpoint === "inbox-triage") {
      const capture = [...notes.values()].find((note) => /(^|\/)inbox\//i.test(note.path)) ?? notes.get(first)!;
      const target = notes.get(second)!;
      const captureHash = smartLinkHash(capture.markdown);
      return respond({
        schemaVersion: 1, generatedAt: "2026-10-04T10:00:00Z", degraded: options.semanticEnabled === false ? "Deterministic Inbox triage is active. Remote generation is not used when Inbox opens; semantic-only matches may be unavailable." : undefined,
        items: [{
          captureId: capture.id, libraryId, captureHash, sourceFingerprint: "triage-source", schemaVersion: 1, title: capture.title, path: capture.path, revision: capture.revision, content: capture.markdown.replace(/^---[\s\S]*?---/, "").replace(/^#.*$/m, "").trim(), createdAt: "2026-10-04T09:00:00Z",
          suggestedType: "Question", confidence: "strong", signals: ["The capture is phrased as a direct question"],
          possibleProjects: [{ path: "Projects/Slate", score: 82, explanation: "2 strong related notes are in this project" }, { path: "Projects/ReplayOps", score: 70, explanation: "A second project uses the same terminology" }],
          possibleFolders: [{ path: "Projects", score: 82, explanation: "Existing folder used by related notes" }],
          concepts: [{ id: "concept-slate", name: "Slate", reason: "Found in this capture" }, { id: "concept-postgresql", name: "PostgreSQL", reason: "Found in this capture" }],
          relatedNotes: [{ noteId: target.id, title: target.title, path: target.path, score: 91, reasons: ["Exact topic match", "Same project"], overlapKind: "partial-overlap" }],
          overlapFindings: [{ id: "overlap-fixture", kind: "partial-overlap", label: "Partial overlap", explanation: "Part of this capture overlaps with an existing note.", targetNoteId: target.id }],
          linkTargets: [{ noteId: target.id, title: target.title, path: target.path, explanation: "Direct concept mention suggests a useful link." }],
          knowledgeGaps: [{ id: "gap-thin", fingerprint: "gap-thin", title: "PostgreSQL operations", summary: "Operational recovery coverage is limited." }],
          suggestedActions: [
            { id: "convert-question", action: "convert-question", label: "Convert to Question", explanation: "The capture is phrased as a direct question.", fingerprint: `v1:${captureHash}:question` },
            { id: `append:${target.id}`, action: "append", label: `Append to ${target.title}`, targetNoteId: target.id, explanation: "Partial overlap · Same project", fingerprint: `v1:${captureHash}:append` },
            { id: "move:Projects", action: "move", label: "Move to Projects", destination: "Projects", explanation: "Existing folder used by related notes", fingerprint: `v1:${captureHash}:move` },
            { id: "keep", action: "keep", label: "Keep as Note", explanation: "Keep the capture intact and mark it reviewed.", fingerprint: `v1:${captureHash}:keep` },
            { id: "create-note", action: "create-note", label: "Create New Note", destination: "Projects", explanation: "Create a separate note while preserving the original capture.", fingerprint: `v1:${captureHash}:create` },
          ], explanation: "Question capture with related material in Projects/Slate.",
        }],
        diagnostics: { inboxItems: 1, analyzed: 1, pending: 0, failed: 0, strongProjectSuggestions: 1, overlapMatches: 1, questionSuggestions: 1, appendOpportunities: 1, deterministicClassifications: 1, optionalModelClassifications: 0, cacheHits: 0, boundsReached: false },
      });
    }
    if (endpoint === "knowledge-gaps") {
      const input = req.postDataJSON() as { filters?: Record<string, string | number>; reviews?: { gaps?: Record<string, { state: KnowledgeGap["reviewState"] }> }; action?: string };
      const source = (id: string, title: string, path: string, excerpt: string, words: number) => ({ noteId: id, title, path, revision: '"r1"', excerpt, explanatoryWords: words, substantial: false });
      const base: Pick<KnowledgeGap, "libraryId" | "schemaVersion" | "reviewState"> = { libraryId, schemaVersion: 1, reviewState: "open" };
      let gaps: KnowledgeGap[] = [
        { ...base, id: "gap-thin", fingerprint: "gap-thin", kind: "thin-coverage", conceptId: "concept-kubernetes", conceptName: "Kubernetes", projectPath: "Projects", noteIds: [first, second], sourceConceptIds: ["concept-kubernetes"], title: "Kubernetes operations", summary: "Kubernetes is referenced across several notes, but current explanations are limited.", evidence: [source(first, "Library architecture", "Projects/Library architecture.md", "Kubernetes hosts the service.", 18), source(second, "Reading list", "Research/Reading list.md", "Review Kubernetes operations.", 12)], signals: ["Referenced in 8 notes", "Appears in 2 projects", "No substantial explanatory source"], coverageLevel: "thin", importanceLevel: "high", recommendedActionType: "create-draft", targetWorkspace: "concepts", targetPayload: { concept: "Kubernetes" } },
        { ...base, id: "gap-fragmented", fingerprint: "gap-fragmented", kind: "fragmented-coverage", conceptId: "concept-postgresql", conceptName: "PostgreSQL", projectPath: "Projects", noteIds: [first, second], sourceConceptIds: ["concept-postgresql"], title: "PostgreSQL transactions", summary: "Knowledge about PostgreSQL transactions appears across brief sources without one substantial explanation.", evidence: [source(first, "Library architecture", "Projects/Library architecture.md", "Transactions are mentioned in storage decisions.", 34), source(second, "Reading list", "Research/Reading list.md", "Isolation is an open reading topic.", 28)], signals: ["Fragmented across 4 brief sources", "No substantial explanatory source"], coverageLevel: "fragmented", importanceLevel: "medium", recommendedActionType: "review-overlap", targetWorkspace: "knowledge-overlap", targetPayload: { noteId: first } },
        { ...base, id: "gap-overview", fingerprint: "gap-overview", kind: "missing-overview", conceptId: null, conceptName: null, projectPath: "Projects", noteIds: [first], sourceConceptIds: [], title: "Projects", summary: "This project contains implementation notes across several documented areas, but no substantial overview was found.", evidence: [source(first, "Library architecture", "Projects/Library architecture.md", "Storage, sync, and deployment implementation notes.", 90)], signals: ["12 project notes", "Documented areas: API, data, deployment", "No substantial overview source"], coverageLevel: "missing", importanceLevel: "high", recommendedActionType: "create-draft", targetWorkspace: "project-brain", targetPayload: { project: "Projects" } },
        { ...base, id: "gap-question", fingerprint: "gap-question", kind: "recurring-unanswered-question", conceptId: "concept-kubernetes", conceptName: "Kubernetes", projectPath: "Projects", noteIds: [first, second], sourceConceptIds: ["concept-kubernetes"], title: "Offline conflict resolution", summary: "Three unresolved questions describe a recurring topic around offline conflict resolution.", evidence: [source(first, "Library architecture", "Projects/Library architecture.md", "How should offline conflicts resolve?", 6), source(second, "Reading list", "Research/Reading list.md", "What wins during an offline merge?", 7)], signals: ["3 related open questions", "No later answer was identified"], coverageLevel: "limited", importanceLevel: "medium", recommendedActionType: "answer-question", targetWorkspace: "knowledge-issues", targetPayload: { noteId: first } },
        { ...base, id: "gap-bridge", fingerprint: "gap-bridge", kind: "missing-bridge-knowledge", conceptId: "concept-kubernetes", conceptName: "Kubernetes", projectPath: "Projects", noteIds: [first, second], sourceConceptIds: ["concept-kubernetes", "concept-postgresql"], title: "Kubernetes and PostgreSQL", summary: "These concepts repeatedly appear together, but no substantial source explains how they interact.", evidence: [source(first, "Library architecture", "Projects/Library architecture.md", "Kubernetes and PostgreSQL are named together.", 24)], signals: ["Named together in 4 project notes", "Connected in the project graph"], coverageLevel: "missing", importanceLevel: "high", recommendedActionType: "create-draft", targetWorkspace: "concepts", targetPayload: { concept: "Kubernetes" } },
      ];
      gaps = gaps.map((gap) => ({ ...gap, reviewState: input.reviews?.gaps?.[gap.fingerprint]?.state ?? "open" }));
      const filters = input.filters ?? {};
      gaps = gaps.filter((gap) => !filters.kind || gap.kind === filters.kind).filter((gap) => !filters.project || gap.projectPath === filters.project).filter((gap) => !filters.concept || gap.conceptId === filters.concept || gap.sourceConceptIds.includes(String(filters.concept))).filter((gap) => !filters.importance || gap.importanceLevel === filters.importance).filter((gap) => !filters.status || gap.reviewState === filters.status).filter((gap) => !filters.search || `${gap.title} ${gap.summary}`.toLowerCase().includes(String(filters.search).toLowerCase()));
      const open = gaps.filter((gap) => gap.reviewState === "open"), pageNumber = Number(filters.page ?? 0), pageSize = Number(filters.limit ?? 20), start = pageNumber * pageSize;
      const kindCounts = Object.fromEntries([...new Set(open.map((gap) => gap.kind))].map((kind) => [kind, open.filter((gap) => gap.kind === kind).length]));
      return respond({ schemaVersion: 1, generatedAt: "2026-10-04T10:00:00Z", page: pageNumber, pageSize, total: gaps.length, items: gaps.slice(start, start + pageSize), summary: { open: open.length, kinds: kindCounts, importance: { high: open.filter((gap) => gap.importanceLevel === "high").length, medium: open.filter((gap) => gap.importanceLevel === "medium").length, low: open.filter((gap) => gap.importanceLevel === "low").length } }, projects: ["Projects"], concepts: [{ id: "concept-kubernetes", name: "Kubernetes" }, { id: "concept-postgresql", name: "PostgreSQL" }], diagnostics: { conceptsAnalyzed: 2, projectsAnalyzed: 1, openGaps: open.length, thinCoverage: 1, fragmentedCoverage: 1, missingOverview: 1, bridgeGaps: 1, recurringQuestions: 1, deterministicFindings: 5, optionalModelClassifications: 0, cacheHits: input.action === "rebuild" ? 0 : 1, pending: 0, failed: 0, boundsReached: false, semanticAvailable: options.semanticEnabled !== false, postgresAvailable: options.postgresAvailable !== false }, degraded: options.postgresAvailable === false ? "Knowledge gaps are available from bounded session analysis; PostgreSQL persistence is unavailable." : options.semanticEnabled === false ? "Deterministic knowledge-gap analysis is active; semantic enrichment is unavailable." : undefined });
    }
    if (endpoint === "health") {
      const input = req.postDataJSON() as { filters?: Record<string, string | number>; reviews?: { health?: Record<string, { state: string }>; knowledge?: Record<string, { state: string }> } };
      const base: Pick<LibraryHealthItem, "libraryId" | "schemaVersion" | "createdAt" | "updatedAt" | "reviewState" | "healthOnly" | "evidenceLevel" | "targetPayload" | "reasons"> = { libraryId, schemaVersion: 1, createdAt: "2026-10-04T10:00:00Z", updatedAt: "2026-10-04T10:00:00Z", reviewState: "open", healthOnly: true, evidenceLevel: "documented", targetPayload: {}, reasons: ["Deterministic fixture evidence"] };
      let items: LibraryHealthItem[] = [
        { ...base, id: "health-broken", fingerprint: "health-broken", category: "links", kind: "broken-link", sourceSystem: "canonical-links", title: "Broken link", summary: "Library architecture contains an unresolved authored link.", noteIds: [first], noteTitles: ["Library architecture"], paths: ["Projects/Library architecture.md"], projectPath: "Projects", conceptIds: ["concept-slate"], priority: "needs-attention", targetWorkspace: "link-health", targetPayload: { noteId: first } },
        { ...base, id: "health-contradiction", fingerprint: "issue-fixture-conflict", category: "consistency", kind: "contradiction", sourceSystem: "knowledge-issues", title: "Current contradiction", summary: "Two current notes record different storage choices.", noteIds: [first, second], noteTitles: ["Library architecture", "Reading list"], paths: ["Projects/Library architecture.md", "Research/Reading list.md"], projectPath: "Projects", conceptIds: ["concept-slate"], priority: "needs-attention", healthOnly: false, evidenceLevel: "strongly-indicated", targetWorkspace: "knowledge-issues", targetPayload: { noteId: first } },
        { ...base, id: "health-duplicate", fingerprint: "overlap-exact", category: "overlap", kind: "exact-duplicate", sourceSystem: "knowledge-overlap", title: "Exact duplicate", summary: "Two notes contain the same material.", noteIds: [first, second], noteTitles: ["Library architecture", "Reading list"], paths: ["Projects/Library architecture.md", "Research/Reading list.md"], projectPath: "Projects", conceptIds: ["concept-slate"], priority: "needs-attention", healthOnly: false, targetWorkspace: "knowledge-overlap", targetPayload: { noteId: first } },
        { ...base, id: "health-missing-link", fingerprint: "link-fixture-mention", category: "relationships", kind: "missing-link", sourceSystem: "smart-linking", title: "Missing relationship", summary: "A direct mention can become an authored link.", noteIds: [first, second], noteTitles: ["Library architecture", "Reading list"], paths: ["Projects/Library architecture.md", "Research/Reading list.md"], projectPath: "Projects", conceptIds: ["concept-slate"], priority: "worth-reviewing", healthOnly: false, evidenceLevel: "strongly-indicated", targetWorkspace: "link-opportunities", targetPayload: { noteId: first } },
        { ...base, id: "health-answered", fingerprint: "issue-fixture-question", category: "consistency", kind: "answered-question", sourceSystem: "knowledge-issues", title: "Possible answer found", summary: "A later source may answer an open question.", noteIds: [first, second], noteTitles: ["Library architecture", "Reading list"], paths: ["Projects/Library architecture.md", "Research/Reading list.md"], projectPath: "Projects", conceptIds: ["concept-slate"], priority: "worth-reviewing", healthOnly: false, evidenceLevel: "possible", targetWorkspace: "knowledge-issues", targetPayload: { noteId: first } },
        { ...base, id: "health-orphan", fingerprint: "health-orphan", category: "structure", kind: "orphan-note", sourceSystem: "canonical-graph", title: "Orphan note", summary: "Reading list has no meaningful authored connections.", noteIds: [second], noteTitles: ["Reading list"], paths: ["Research/Reading list.md"], projectPath: "Research", conceptIds: [], priority: "informational", targetWorkspace: "note", targetPayload: { noteId: second } },
        { ...base, id: "health-asset", fingerprint: "health-asset", category: "assets", kind: "unreferenced-asset", sourceSystem: "canonical-assets", title: "Unreferenced asset", summary: "diagram.png has no current note references.", noteIds: [], noteTitles: [], paths: [], projectPath: null, conceptIds: [], priority: "worth-reviewing", targetWorkspace: "assets", targetPayload: { assetId: "asset-fixture" } },
        { ...base, id: "health-index", fingerprint: "health-index", category: "intelligence", kind: "failed-indexing", sourceSystem: "intelligence-jobs", title: "Indexing needs attention", summary: "One persistent indexing job failed.", noteIds: [], noteTitles: [], paths: [], projectPath: null, conceptIds: [], priority: "needs-attention", healthOnly: false, targetWorkspace: "diagnostics", reasons: ["Persistent indexing failure"] },
        { ...base, id: "health-concept", fingerprint: "health-concept", category: "concepts", kind: "ambiguous-concept-identity", sourceSystem: "concepts", title: "Concept identity needs review", summary: "Slate has several derived aliases.", noteIds: [first], noteTitles: ["Library architecture"], paths: ["Projects/Library architecture.md"], projectPath: "Projects", conceptIds: ["concept-slate"], priority: "worth-reviewing", evidenceLevel: "possible", targetWorkspace: "concepts", targetPayload: { concept: "Slate" } },
        { ...base, id: "health-coverage", fingerprint: "gap-thin", category: "coverage", kind: "thin-coverage", sourceSystem: "knowledge-gaps", title: "Thin concept coverage", summary: "Kubernetes is referenced often but explained briefly.", noteIds: [first, second], noteTitles: ["Library architecture", "Reading list"], paths: ["Projects/Library architecture.md", "Research/Reading list.md"], projectPath: "Projects", conceptIds: ["concept-kubernetes"], priority: "worth-reviewing", healthOnly: false, evidenceLevel: "strongly-indicated", targetWorkspace: "knowledge-gaps", targetPayload: { fingerprint: "gap-thin", concept: "concept-kubernetes" } },
      ];
      items = items.map((item) => ({ ...item, reviewState: (input.reviews?.health?.[item.fingerprint]?.state ?? (item.sourceSystem === "knowledge-issues" ? input.reviews?.knowledge?.[item.fingerprint]?.state : undefined) ?? "open") as LibraryHealthItem["reviewState"] }));
      const filters = input.filters ?? {};
      items = items.filter((item) => !filters.category || item.category === filters.category).filter((item) => !filters.priority || item.priority === filters.priority).filter((item) => !filters.status || item.reviewState === filters.status).filter((item) => !filters.project || item.projectPath === filters.project).filter((item) => !filters.concept || item.conceptIds.includes(String(filters.concept))).filter((item) => !filters.noteId || item.noteIds.includes(String(filters.noteId))).filter((item) => !filters.search || `${item.title} ${item.summary} ${item.noteTitles.join(" ")} ${item.paths.join(" ")}`.toLowerCase().includes(String(filters.search).toLowerCase()));
      const allOpen = items.filter((item) => item.reviewState === "open"); const pageNumber = Number(filters.page ?? 0), pageSize = Number(filters.limit ?? 25), start = pageNumber * pageSize;
      const count = (key: string, value: string) => allOpen.filter((item) => item[key as keyof typeof item] === value).length;
      return respond({ schemaVersion: 1, generatedAt: "2026-10-04T10:00:00Z", page: pageNumber, pageSize, total: items.length, items: items.slice(start, start + pageSize), summary: { open: allOpen.length, priorities: { "needs-attention": count("priority", "needs-attention"), "worth-reviewing": count("priority", "worth-reviewing"), informational: count("priority", "informational") }, categories: { links: count("category", "links"), consistency: count("category", "consistency"), overlap: count("category", "overlap"), relationships: count("category", "relationships"), structure: count("category", "structure"), coverage: count("category", "coverage"), assets: count("category", "assets"), metadata: count("category", "metadata"), concepts: count("category", "concepts"), intelligence: count("category", "intelligence") } }, projects: ["Projects", "Research"], concepts: [{ id: "concept-slate", name: "Slate" }, { id: "concept-kubernetes", name: "Kubernetes" }], diagnostics: { sourcesQueried: 9, itemsAggregated: 10, openItems: allOpen.length, needsAttention: count("priority", "needs-attention"), worthReviewing: count("priority", "worth-reviewing"), informational: count("priority", "informational"), healthOnlyFindings: 4, aggregationDurationMs: 3, failedSourceQueries: options.healthPartialFailure ? ["assets"] : [], lastRefresh: "2026-10-04T10:00:00Z", bounded: false }, degraded: options.healthPartialFailure ? "Partial Library Health is shown. Unavailable sources: assets." : undefined });
    }
    if (endpoint === "concepts") {
      const input = req.postDataJSON() as { identity?: string };
      const member = (source: Note, strength: string, score: number, reasons: string[], projectPath: string | null, extras: Record<string, unknown> = {}) => ({ noteId: source.id, revision: source.revision, title: source.title, path: source.path, strength, score, reasons, excerpt: source.markdown.replace(/^---[\s\S]*?---/, "").trim().slice(0, 180), projectPath, tags: ["knowledge"], timestamp: "2026-10-03T10:00:00Z", decisions: [], questions: [], ...extras });
      const kubernetesMembers = [member(notes.get(first)!, "primary", 116, ["Dedicated concept note", "Contains a decision"], "Projects", { decisions: ["Use Kubernetes for Slate deployment."], questions: ["Should Kubernetes use local embeddings?"] }), member(notes.get(second)!, "supporting", 54, ["Repeated technical reference"], "Research")];
      const concepts = [
        { id: "concept-kubernetes", canonicalName: "Kubernetes", normalizedName: "kubernetes", aliases: ["k8s"], kind: "Technology", sourceCount: 2, currentSourceCount: 2, firstSeen: "2026-09-28T10:00:00Z", lastSeen: "2026-10-03T10:00:00Z", projectPaths: ["Projects", "Research"], tags: ["knowledge"], importanceSignals: ["Dedicated note", "Appears across 2 projects", "Contains 1 decisions", "Referenced in 2 notes"], contentHash: "concept-hash-k8s", schemaVersion: 1, members: kubernetesMembers, relationships: [{ sourceConceptId: "concept-kubernetes", targetConceptId: "concept-postgresql", targetName: "PostgreSQL", relationshipType: "used_with", signals: ["Connected by an authored note link", "Named together in 2 source notes"], explanation: "Slate deployment notes use PostgreSQL alongside Kubernetes across two projects.", sourceCount: 2, projectCount: 2, sourceNoteIds: [first, second], schemaVersion: 1 }] },
        { id: "concept-postgresql", canonicalName: "PostgreSQL", normalizedName: "postgresql", aliases: ["Postgres"], kind: "Database", sourceCount: 2, currentSourceCount: 2, firstSeen: "2026-09-28T10:00:00Z", lastSeen: "2026-10-03T10:00:00Z", projectPaths: ["Projects"], tags: ["storage"], importanceSignals: ["Referenced in 2 notes"], contentHash: "concept-hash-postgres", schemaVersion: 1, members: kubernetesMembers, relationships: [{ sourceConceptId: "concept-postgresql", targetConceptId: "concept-kubernetes", targetName: "Kubernetes", relationshipType: "used_with", signals: ["Connected by an authored note link"], explanation: "PostgreSQL appears with Kubernetes in deployment architecture.", sourceCount: 2, projectCount: 1, sourceNoteIds: [first, second], schemaVersion: 1 }] },
      ];
      const diagnostics = { conceptsIndexed: 2, relationships: 2, aliases: 2, mergedDerivedIdentities: 0, notesProcessed: 2, pending: 0, failed: 0, reused: 2, rebuiltThisProcess: 0, boundsReached: false };
      if (!input.identity) return respond({ schemaVersion: 1, generatedAt: "2026-10-04T10:00:00Z", concepts, diagnostics, degraded: options.generationEnabled === false ? "Deterministic concept pages are active; generation is optional." : undefined });
      const concept = concepts.find((item) => item.canonicalName.toLowerCase() === input.identity!.toLowerCase() || item.aliases.some((alias) => alias.toLowerCase() === input.identity!.toLowerCase()));
      if (!concept) return respond({ error: "Concept is not available" }, 404);
      const issueClaim = { claimId: "claim-concept", noteId: first, revision: '"r1"', path: "Projects/Library architecture.md", title: "Library architecture", heading: "Architecture", text: "Kubernetes version is 1.30.", normalizedSubject: "kubernetes", normalizedPredicate: "version", normalizedObject: "1.30", claimType: "version", timestamp: "2026-10-03T10:00:00Z", current: true, projectPath: "Projects", component: "platform", tags: [], contentHash: "claim-hash", extractionMethod: "heading", confidence: "strong", negative: false };
      return respond({ concept, keyNotes: concept.members, projects: [{ path: "Projects", sourceCount: 1, importantNote: concept.members[0], decisions: ["Use Kubernetes for Slate deployment."], questions: ["Should Kubernetes use local embeddings?"], recentActivity: "2026-10-03T10:00:00Z" }], decisions: [{ text: "Use Kubernetes for Slate deployment.", noteId: first, title: "Library architecture", path: "Projects/Library architecture.md" }], questions: [{ text: "Should Kubernetes use local embeddings?", noteId: first, title: "Library architecture", path: "Projects/Library architecture.md" }], timeline: [{ timestamp: "2026-10-03T10:00:00Z", label: "Documented deployment decision", noteId: first, title: "Library architecture", path: "Projects/Library architecture.md" }], issues: [{ issueId: "concept-issue", fingerprint: "concept-issue", kind: "version", evidence: "possible-conflict", title: "Version conflict", explanation: "Current sources record different versions.", state: "open", sourceA: issueClaim, sourceB: { ...issueClaim, claimId: "claim-concept-2", noteId: second, path: "Research/Reading list.md", title: "Reading list", normalizedObject: "1.31", contentHash: "claim-hash-2" }, staleClaimId: null, projectPath: "Projects", currentCurrent: true, createdAt: "2026-10-04T10:00:00Z" }], overlaps: [{ fingerprint: "concept-overlap", noteA: { noteId: first }, noteB: { noteId: second } }], linkOpportunities: [{ fingerprint: "concept-link", sourceNoteId: first, target: { noteId: second } }], graph: { nodes: [{ id: concept.id, name: concept.canonicalName, kind: concept.kind }, ...concept.relationships.map((relationship) => ({ id: relationship.targetConceptId, name: relationship.targetName, kind: "Concept" }))], edges: concept.relationships, bounded: false }, diagnostics, degraded: options.generationEnabled === false ? "Deterministic concept pages are active; generation is optional." : undefined });
    }
    if (endpoint === "link-opportunities") {
      const input = req.postDataJSON() as { scope: { kind: "library" | "project" | "note"; noteId?: string; path?: string } };
      const source = notes.get(first)!; const target = notes.get(second)!; const from = source.markdown.indexOf("stable identities");
      const targetValue = { noteId: target.id, revision: target.revision, contentHash: smartLinkHash(target.markdown), title: target.title, path: target.path, heading: null, noteType: "reference" };
      const suggestion = (fingerprint: string, suggestionType: string, explanation: string, overrides: Record<string, unknown> = {}) => ({ id: fingerprint, fingerprint, sourceNoteId: source.id, sourceRevision: source.revision, sourceHash: smartLinkHash(source.markdown), sourceTitle: source.title, sourcePath: source.path, target: targetValue, alternateTargets: [], sourceHeading: "Principles", sourceExcerpt: "Use stable identities for links.", sourceRange: { from, to: from + "stable identities".length }, existingText: "stable identities", suggestedMarkdown: "[[Reading list|stable identities]]", suggestionType, suggestedLinkKind: "wiki", signals: [suggestionType === "mention" ? "Exact alias match" : suggestionType === "graph_bridge" ? "Two-hop authored graph path" : "Same project", "Shared architecture claims"], explanation, status: "open", ambiguous: false, hasKnowledgeIssue: fingerprint === "link-conflict", overlapKind: fingerprint === "link-partial" ? "partial-overlap" : null, projectPath: "Projects", createdAt: "2026-10-04T10:00:00Z", schemaVersion: 1, ...overrides });
      let suggestions = [
        suggestion("link-direct", "mention", "This phrase exactly matches an existing note alias."),
        suggestion("link-graph", "graph_bridge", "These notes are connected through an authored neighbor and share specific knowledge.", { sourceRange: undefined, existingText: undefined, suggestedMarkdown: undefined }),
        suggestion("link-project", "project_relationship", "These notes belong to the same project and describe connected project knowledge.", { sourceRange: undefined, existingText: undefined, suggestedMarkdown: undefined }),
        suggestion("link-conflict", "related", "Related, but these notes currently disagree.", { sourceRange: undefined, existingText: undefined, suggestedMarkdown: undefined }),
        suggestion("link-better", "better_target", "The current link points to an older note; this target appears current.", { sourceExcerpt: "Review [[Original architecture]].", existingText: "[[Original architecture]]", suggestedMarkdown: "[[Reading list|Original architecture]]", sourceRange: undefined }),
      ];
      if (input.scope.kind === "note") suggestions = input.scope.noteId === source.id ? suggestions : [];
      if (input.scope.kind === "project") suggestions = input.scope.path === "Projects" || input.scope.path === "inbox" ? suggestions : [];
      return respond({ schemaVersion: 1, generatedAt: "2026-10-04T10:00:00Z", scope: input.scope, suggestions, diagnostics: { notesAnalyzed: 2, openSuggestions: suggestions.length, directMentions: 1, graphOpportunities: 1, projectOpportunities: 1, betterTargets: 1, dismissed: 0, inserted: 0, candidatesCheckedThisProcess: 8, deterministicFindings: suggestions.length, modelClassifications: 0, cacheHits: 0, pendingJobs: 0, failedJobs: 0, bounded: false }, degraded: options.generationEnabled === false ? "Deterministic relationship analysis is active; no generation provider is required." : undefined });
    }
    if (endpoint === "knowledge-overlap") {
      const input = req.postDataJSON() as { scope: { kind: string; noteId?: string; path?: string } };
      const overlapNote = (source: Note, suffix: string, headings: string[]) => ({ noteId: source.id, revision: source.revision, path: source.path, title: source.title, contentHash: `overlap-hash-${suffix}`, noteType: null, timestamp: suffix === "a" ? "2025-09-28T10:00:00Z" : "2026-10-03T10:00:00Z", projectPath: "Projects", headings, sections: headings.map((heading, index) => ({ heading, text: heading === "Storage" ? "Markdown is canonical and Git stores history." : `${heading} retains useful project knowledge.`, normalized: heading.toLowerCase(), hash: `${suffix}-${index}`, tokens: heading.toLowerCase().split(" ") })), claims: ["slate|uses|markdown"], normalizedContent: headings.join("\n"), wordCount: 80 });
      const a = overlapNote(notes.get(first)!, "a", ["Storage", "Search", "Benchmark results"]);
      const b = overlapNote(notes.get(second)!, "b", ["Storage", "Search", "Current decision"]);
      const section = (heading: string, suffix: string) => ({ heading, text: heading === "Storage" ? "Markdown is canonical and Git stores history." : `${heading} is unique material for ${suffix}.`, normalized: heading.toLowerCase(), hash: `${suffix}-${heading}`, tokens: heading.toLowerCase().split(" ") });
      const finding = (fingerprint: string, kind: string, label: string, shared = [section("Storage", "shared")]) => ({ id: fingerprint, fingerprint, kind, state: "open", label, explanation: kind === "possibly-absorbed" ? "Most of the older note appears in a newer current note, but unique material remains and should be reviewed." : "These notes share substantial knowledge while retaining identifiable unique sections.", noteA: a, noteB: b, sharedSections: shared, uniqueSectionsA: [section("Benchmark results", "A")], uniqueSectionsB: [section("Current decision", "B")], sharedClaims: ["slate|uses|markdown"], deterministicSignals: ["Bounded deterministic fixture"], classification: "deterministic", projectPath: "Projects", hasKnowledgeIssue: kind === "near-duplicate", createdAt: "2026-10-04T10:00:00Z", schemaVersion: 1 });
      let findings = [finding("overlap-exact", "exact-duplicate", "Exact duplicate"), finding("overlap-near", "near-duplicate", "Near duplicate"), finding("overlap-partial", "partial-overlap", "Partial overlap"), finding("overlap-absorbed", "possibly-absorbed", "Possibly absorbed"), finding("overlap-fragmented", "fragmented", "Possible consolidation opportunity")];
      if (input.scope.kind === "note") findings = findings.filter((item) => item.noteA.noteId === input.scope.noteId || item.noteB.noteId === input.scope.noteId);
      return respond({ schemaVersion: 1, generatedAt: "2026-10-04T10:00:00Z", scope: input.scope, findings, diagnostics: { exactDuplicates: 1, nearDuplicates: 1, partialOverlaps: 1, possiblyAbsorbed: 1, consolidationOpportunities: 1, openReviewItems: findings.length, reviewedItems: 0, pairsAnalyzedThisProcess: 8, deterministicClassifications: findings.length, modelClassifications: 0, cacheHits: 0, pendingJobs: 0, failedJobs: 0, bounded: false }, degraded: options.generationEnabled === false ? "Deterministic overlap analysis is active. Optional model classification is unavailable." : undefined });
    }
    if (endpoint === "knowledge-issues") {
      const input = req.postDataJSON() as { scope: { kind: string; noteId?: string; path?: string } };
      const claim = (suffix: string, source: Note, text: string, object: string, date: string) => ({ claimId: `claim-${suffix}`, noteId: source.id, revision: source.revision, path: source.path, title: source.title, heading: "Architecture", text, normalizedSubject: "slate storage", normalizedPredicate: "uses", normalizedObject: object, claimType: "architecture", timestamp: date, current: true, projectPath: "Projects", component: "database", tags: [], contentHash: `hash-${suffix}`, extractionMethod: "heading", confidence: "strong", negative: false });
      const sourceA = claim("a", notes.get(first)!, "Slate storage uses SQLite.", "sqlite", "2026-09-28T10:00:00Z");
      const sourceB = claim("b", notes.get(second)!, "Slate storage uses PostgreSQL.", "postgresql", "2026-10-03T10:00:00Z");
      const issue = (fingerprint: string, kind: string, evidence: string, title: string, explanation: string, a = sourceA, b = sourceB) => ({ issueId: fingerprint, fingerprint, kind, evidence, title, explanation, state: "open", sourceA: a, sourceB: b, staleClaimId: a.claimId, projectPath: "Projects", currentCurrent: true, createdAt: "2026-10-04T10:00:00Z" });
      let issues = [
        issue("issue-fixture-conflict", "architecture", "strong-conflict", "Architecture conflict", "A newer current source records a different architecture for the same subject."),
        issue("issue-fixture-version", "version", "possible-conflict", "Version conflict", "Two current sources record different versions for the same runtime.", { ...sourceA, claimId: "claim-v1", normalizedPredicate: "version", normalizedObject: "20", text: "Node version is 20." }, { ...sourceB, claimId: "claim-v2", normalizedPredicate: "version", normalizedObject: "24", text: "Node version is 24." }),
        issue("issue-fixture-superseded", "supersession", "likely-superseded", "Likely superseded", "A later source explicitly replaces this approach."),
        issue("issue-fixture-question", "answered-question", "possible-stale", "Possible answer found", "A later source may answer a question that is still recorded as open."),
      ];
      if (input.scope.kind === "note") issues = issues.filter((item) => item.sourceA.noteId === input.scope.noteId || item.sourceB.noteId === input.scope.noteId);
      return respond({ schemaVersion: 1, generatedAt: "2026-10-04T10:00:00Z", scope: input.scope, issues, diagnostics: { claimsIndexed: 8, openIssues: issues.length, resolvedIssues: 0, dismissedIssues: 0, pendingAnalysis: 0, failedAnalysis: 0, pairsCheckedThisProcess: 12, deterministicFindings: issues.length, aiClassifiedPairs: 0, cachedClassifications: 0, bounded: false }, degraded: options.generationEnabled === false ? "Deterministic analysis is active. Optional model classification is unavailable or was not needed." : undefined });
    }
    if (endpoint === "project-brain") {
      const input = req.postDataJSON() as { action: string; path: string; kind?: string; refresh?: boolean };
      projectRequests.push(input);
      const source = { citationId: "S1", noteId: first, title: "Library architecture", path: "Projects/Library architecture.md", heading: "Principles", ordinal: 0, revision: '"r1"', excerpt: "Markdown is the source of truth. Stable identities connect notes.", score: 1 };
      if (input.action === "generate") {
        if (options.generationEnabled === false) return respond({ error: "AI synthesis isn't configured." }, 503);
        const resume = input.kind === "resume";
        return respond({ kind: input.kind, markdown: resume ? "## Project\nSlate is a private Markdown workspace. [S1]\n\n## Where Things Stand\nThe storage architecture is documented. [S1]\n\n## Recent Work\nThe architecture note was updated. [S1]\n\n## Key Decisions\nMarkdown remains the source of truth. [S1]\n\n## Open Questions\nSync retry remains open. [S1]\n\n## Known Problems\nRenderer permissions need review. [S1]\n\n## What to Read First\nLibrary architecture. [S1]\n\n## Possible Next Context\nReview sync behavior. [S1]" : "Slate is a private Markdown knowledge workspace built around stable note identities. [S1]", sources: [source], citations: ["S1"], generatedAt: "2026-10-03T10:00:00Z", cached: false, cacheKey: "fixture" });
      }
      const evidence = (sourceClass: string, title = "Library architecture") => ({ noteId: first, title, path: "Projects/Library architecture.md", revision: '"r1"', heading: "Principles", excerpt: "Markdown is the source of truth. Stable identities connect notes.", sourceClass, type: sourceClass, status: sourceClass === "question" ? "open" : "active", timestamp: "2026-10-03T10:00:00Z" });
      return respond({ schemaVersion: 1, path: input.path, name: input.path.split("/").at(-1), noteCount: 1, folderCount: 1, bounded: false, status: "Active", lastMeaningfulChange: "2026-10-03T10:00:00Z", openQuestionCount: 1, sections: { overview: [evidence("overview")], current: [evidence("recent")], decisions: [evidence("decision", "Use Markdown storage")], questions: [evidence("question", "How should sync retry?")], architecture: [evidence("architecture")], ideas: [evidence("idea", "Offline cache")], experiments: [evidence("experiment", "Index experiment")], failures: [evidence("failure", "WebView failure")], risks: [evidence("risk", "Renderer permissions")], important: [evidence("important")] }, timeline: [{ id: "event-1", noteId: first, title: "Library architecture", path: "Projects/Library architecture.md", timestamp: "2026-10-03T10:00:00Z", label: "Document library structure", commit: "a".repeat(40) }], graph: { nodes: [{ id: first, title: "Library architecture", path: "Projects/Library architecture.md", kind: "architecture" }], edges: [], limited: false }, relatedOutside: [{ id: second, title: "Reading list", path: "Research/Reading list.md", score: .8, reasons: ["Related storage research"] }], sources: [source], sourceFingerprint: "fixture-fingerprint", generatedAt: "2026-10-03T10:00:00Z", provider: { available: options.generationEnabled !== false, name: options.generationEnabled === false ? "disabled" : "deterministic-test", model: options.generationEnabled === false ? "disabled" : "grounded-v1" } });
    }
    if (endpoint === "evolution") {
      const input = req.postDataJSON() as { action: string; scope: { kind: string; topic?: string; path?: string; noteId?: string; noteIds?: string[] }; refresh?: boolean };
      evolutionRequests.push(input);
      const before = { citationId: "S1", noteId: first, title: "Library architecture", path: "Projects/Library architecture.md", revision: "a".repeat(40), commit: "a".repeat(40), timestamp: "2026-09-28T10:00:00Z", heading: "Storage decisions", excerpt: "The first design used a local polling loop.", state: "historical" };
      const after = { citationId: "S2", noteId: first, title: "Library architecture", path: "Projects/Library architecture.md", revision: '"r1"', commit: null, timestamp: "2026-10-03T10:00:00Z", heading: "Storage decisions", excerpt: "The implementation changed to a durable event queue.", state: "current" };
      const events = [
        { id: "event-1", type: "ArchitectureChanged", confidence: "implementation", label: "Changed implementation", title: "Implementation changed", summary: "Library architecture: Storage decisions changed.", timestamp: "2026-10-03T10:00:00Z", dateSource: "git", noteId: first, noteTitle: "Library architecture", path: "Projects/Library architecture.md", before, after, rationale: "The queue preserves drafts during retries.", changedHeadings: ["Storage decisions"] },
        { id: "event-2", type: "DecisionAdded", confidence: "documented", label: "Documented decision", title: "Decision recorded", summary: "Markdown became the canonical store.", timestamp: "2026-10-02T10:00:00Z", dateSource: "git", noteId: first, noteTitle: "Library architecture", path: "Projects/Library architecture.md", before, after, rationale: "Readable source files were required.", changedHeadings: ["Decision"] },
        { id: "event-3", type: "QuestionAnswered", confidence: "documented", label: "Open question", title: "Question answered", summary: "The retry strategy was answered.", timestamp: "2026-10-01T10:00:00Z", dateSource: "metadata", noteId: first, noteTitle: "Library architecture", path: "Projects/Library architecture.md", before, after, rationale: null, changedHeadings: ["Answer"] },
        { id: "event-4", type: "Superseded", confidence: "explicit", label: "Superseded idea", title: "Earlier idea superseded", summary: "Polling was explicitly superseded.", timestamp: "2026-09-30T10:00:00Z", dateSource: "git", noteId: first, noteTitle: "Library architecture", path: "Projects/Library architecture.md", before, after, rationale: null, changedHeadings: ["Earlier approach"] },
        { id: "event-5", type: "PossibleShift", confidence: "possible", label: "Possible shift", title: "Possible shift in emphasis", summary: "Reading moved toward reliability.", timestamp: null, dateSource: null, noteId: second, noteTitle: "Reading list", path: "Research/Reading list.md", before: null, after: { ...after, citationId: "S3", noteId: second, title: "Reading list", path: "Research/Reading list.md", excerpt: "Research increasingly discusses reliable storage." }, rationale: null, changedHeadings: ["Document"] },
      ];
      if (input.action === "generate") {
        if (options.generationEnabled === false) return respond({ error: "AI synthesis isn't configured." }, 503);
        return respond({ markdown: "## Early View\nThe design used polling. [S1]\n\n## What Changed\nA durable event queue replaced polling. [S2]\n\n## Current View\nThe queue is current. [S2]\n\n## Key Turning Points\nThe implementation changed. [S1] [S2]\n\n## Unresolved Questions\nReason not documented. [S2]", citations: ["S1", "S2"], sources: [before, after], generatedAt: "2026-10-04T10:00:00Z", cached: false, cacheKey: "fixture-evolution" });
      }
      return respond({ schemaVersion: 1, scope: input.scope, title: input.scope.kind === "topic" ? input.scope.topic : "Library architecture", events, current: [{ noteId: first, title: "Library architecture", path: "Projects/Library architecture.md", revision: '"r1"', status: "active", type: "architecture", updatedAt: "2026-10-03T10:00:00Z", excerpt: "Markdown is the source of truth and a durable event queue handles retries." }], sources: [before, after], noteCount: 1, revisionCount: 3, bounded: false, sourceFingerprint: "fixture-evolution-fingerprint", generatedAt: "2026-10-04T10:00:00Z", provider: { available: options.generationEnabled !== false, name: options.generationEnabled === false ? "disabled" : "deterministic-test", model: options.generationEnabled === false ? "disabled" : "grounded-v1" }, degraded: options.generationEnabled === false ? "AI synthesis is not configured. The deterministic timeline, comparisons, and current view remain available." : undefined });
    }
    if (endpoint === "ask") {
      const input = req.postDataJSON() as { question: string; policy: "strict" | "general"; scope: { kind: string; noteId?: string; noteIds?: string[]; path?: string } };
      askRequests.push(input);
      if (/slow/i.test(input.question)) await new Promise((resolve) => setTimeout(resolve, 1_500));
      let scoped = Array.from(notes.values());
      if (input.scope.kind === "note") scoped = scoped.filter((note) => note.id === input.scope.noteId);
      if (input.scope.kind === "selected") scoped = scoped.filter((note) => input.scope.noteIds?.includes(note.id));
      if (input.scope.kind === "folder" || input.scope.kind === "project") scoped = scoped.filter((note) => note.path === input.scope.path || note.path.startsWith(`${input.scope.path}/`));
      if (/quantum chromodynamics|missing evidence/i.test(input.question)) scoped = [];
      const sources = scoped.slice(0, 2).map((note, index) => ({ citationId: `S${index + 1}`, noteId: note.id, title: note.title, path: note.path, heading: index ? null : "Principles", ordinal: index, revision: note.revision, excerpt: note.markdown.replace(/^---[\s\S]*?---/, "").trim().slice(0, 220), score: 1 - index * .1 }));
      const answer = !sources.length
        ? "I couldn't find enough in your Slate library to answer that."
        : input.policy === "general"
          ? `## From your library\n\nSlate keeps Markdown as the source of truth. [S1]\n\n## General context\n\nGeneral context is clearly separated from library evidence.`
          : `Slate keeps Markdown as the source of truth and uses stable identities for links. [S1]${sources[1] ? " Related reading covers distributed systems. [S2]" : ""}`;
      const events = [
        { type: "retrieval", message: "Searching your Slate sources…" },
        { type: "sources", sources },
        ...answer.match(/.{1,34}(?:\s|$)/g)!.map((text) => ({ type: "delta", text })),
        { type: "done", citations: sources.map((source) => source.citationId), usage: { inputTokens: 120, outputTokens: 36 } },
      ];
      return route.fulfill({ status: 200, contentType: "text/event-stream; charset=utf-8", body: events.map((event) => `data: ${JSON.stringify(event)}\n\n`).join("") }).catch(() => undefined);
    }
    if (endpoint === "search") {
      const input = req.postDataJSON() as { query: string; mode: "hybrid" | "lexical" | "semantic" };
      const term = input.query.toLowerCase();
      const lexical = Array.from(notes.values()).filter((n) => (n.title + " " + n.markdown).toLowerCase().includes(term));
      const semantic = /durable|knowledge|reliable|storage|architecture/.test(term) ? [notes.get(first)!] : /reading|distributed/.test(term) ? [notes.get(second)!] : [];
      const selected = input.mode === "lexical" ? lexical : input.mode === "semantic" ? semantic : [...new Map([...lexical, ...semantic].map((n) => [n.id, n])).values()];
      return respond({ query: input.query, mode: input.mode, effectiveMode: input.mode, page: 0, pageSize: 20, total: selected.length, results: selected.map((n) => ({ id: n.id, title: n.title, path: n.path, revision: n.revision, snippet: n.markdown.slice(0, 180), heading: "Principles", match: lexical.some((hit) => hit.id === n.id) ? "both" : "meaning", score: 1 })) });
    }
    if (endpoint === "related") {
      const note = notes.get(first)!;
      return respond([{ id: second, title: "Reading list", path: "Research/Reading list.md", score: .8, reasons: ["Links to this note", `Similar meaning to ${note.title}`] }]);
    }
    return respond({}, 404);
  });
  await page.route("**/api/slate/**", async (route: Route) => {
    const req = route.request();
    const url = new URL(req.url());
    const path = url.pathname.replace("/api/slate/", "");
    const method = req.method();
    let body: Record<string, unknown> = {};
    if (method !== "GET" && req.headers()["content-type"]?.includes("json"))
      body = req.postDataJSON();
    requests.push({ path, method, body });
    const respond = (data: unknown, status = 200) =>
      route.fulfill({
        status,
        contentType: "application/json",
        body: JSON.stringify(data),
      });
    if (!req.headers().authorization?.startsWith("Bearer "))
      return respond({}, 401);
    if (path === "v1/status")
      return respond({
        libraryId,
        noteCount: notes.size,
        serverVersion: "0.4.1-test",
        indexState: "Ready",
        git: { state: "Synced", pending: false },
      });
    if (path === "v1/sync") return respond({ state: "Synced", pending: false });
    if (path === "v1/library/refresh") return route.fulfill({ status: 204 });
    if (path === "v1/library/bulk/preview") {
      const operationId = String(body.operationId);
      const operation = String(body.operation);
      const destination = String(body.destinationFolderPath ?? "");
      const selected = body.paths as string[];
      const items = selected.map((sourcePath) => {
        const isDirectory = folders.has(sourcePath);
        const filename = sourcePath.split("/").at(-1)!;
        const parent = sourcePath.includes("/")
          ? sourcePath.slice(0, sourcePath.lastIndexOf("/"))
          : "";
        const destinationPath =
          operation === "delete"
            ? null
            : operation === "duplicate"
              ? [parent, `${filename} copy`].filter(Boolean).join("/")
              : [destination, filename].filter(Boolean).join("/");
        return { sourcePath, destinationPath, isDirectory };
      });
      const plan = { operation, items, fingerprint: `fixture-${operationId}` };
      bulkPlans.set(operationId, plan);
      return respond({
        operationId,
        operation,
        items,
        noteCount: selected.reduce(
          (count, selectedPath) =>
            count +
            Array.from(notes.values()).filter(
              (note) =>
                note.path === selectedPath || note.path.startsWith(`${selectedPath}/`),
            ).length,
          0,
        ),
        fingerprint: plan.fingerprint,
        repairs: [],
      });
    }
    if (path === "v1/library/bulk/apply") {
      const operationId = String(body.operationId);
      const plan = bulkPlans.get(operationId);
      if (!plan || plan.fingerprint !== body.fingerprint) return respond({}, 412);
      for (const item of plan.items) {
        const affected = Array.from(notes.values()).filter(
          (note) =>
            note.path === item.sourcePath || note.path.startsWith(`${item.sourcePath}/`),
        );
        if (plan.operation === "delete" || plan.operation === "move") {
          for (const note of affected) notes.delete(note.id);
          if (item.isDirectory) folders.delete(item.sourcePath);
        }
        if (item.destinationPath && plan.operation !== "delete") {
          if (item.isDirectory) folders.add(item.destinationPath);
          for (const note of affected) {
            const copied = {
              ...note,
              id: plan.operation === "move" ? note.id : crypto.randomUUID(),
              path: item.destinationPath + note.path.slice(item.sourcePath.length),
            };
            notes.set(copied.id, copied);
          }
        }
      }
      return respond({
        operationId,
        state: "completed",
        items: plan.items,
        noteCount: plan.items.length,
      });
    }
    if (path === "v1/library") {
      const folder = url.searchParams.get("path") ?? "";
      if (folder && !folders.has(folder)) return respond({}, 404);
      return respond({
        path: folder,
        entries: [
          ...Array.from(folders)
            .filter(
              (f) =>
                (f.includes("/") ? f.slice(0, f.lastIndexOf("/")) : "") ===
                folder,
            )
            .map((f) => ({
              name: f.split("/").at(-1),
              path: f,
              isDirectory: true,
              id: null,
              title: null,
            })),
          ...Array.from(notes.values())
            .filter(
              (n) =>
                (n.path.includes("/")
                  ? n.path.slice(0, n.path.lastIndexOf("/"))
                  : "") === folder,
            )
            .map(getEntry),
        ],
        nextPage: null,
      });
    }
    if (path === "v1/search") {
      const term = (url.searchParams.get("q") ?? "").toLowerCase();
      const results = Array.from(notes.values())
        .filter((n) =>
          (n.title + " " + n.markdown).toLowerCase().includes(term),
        )
        .map((n) => ({
          ...n,
          snippet: n.markdown.replace(/^---[\s\S]*?---/, "").slice(0, 130),
          score: 1,
        }));
      return respond({
        query: term,
        page: 0,
        pageSize: 20,
        total: results.length,
        results,
      });
    }
    if (path === "v1/views/unanswered") {
      const note = notes.get(first)!;
      return respond({
        query: "is:unanswered",
        page: 0,
        pageSize: 20,
        total: 1,
        results: [
          {
            id: note.id,
            title: note.title,
            path: note.path,
            revision: note.revision,
            snippet: "An open question from the library.",
            score: 1,
          },
        ],
        searchVersion: 1,
      });
    }
    if (path.startsWith("v1/rediscovery/")) {
      const note = notes.get(second)!;
      return respond({
        view: path.split("/").at(-1),
        page: 0,
        total: 1,
        results: [
          {
            id: note.id,
            title: note.title,
            path: note.path,
            reason: "You have not opened this note recently.",
            date: "2026-09-28",
          },
        ],
      });
    }
    if (path === "v1/workflows/daily") {
      const date = String(body.date);
      const id = "00000000-0000-4000-8000-000000000003";
      const existing = notes.get(id);
      if (existing) return respond(existing);
      folders.add("Daily");
      const note = {
        id,
        path: `Daily/${date}.md`,
        title: date,
        markdown: `---\nid: ${id}\ntype: daily\n---\n\n# ${date}\n`,
        revision: '"r1"',
      };
      notes.set(id, note);
      return respond(note, 201);
    }
    if (path === "v1/captures") {
      const id = String(body.captureId);
      if (notes.has(id)) return respond(notes.get(id));
      const content = String(body.content);
      const note = {
        id,
        path: `inbox/Thought-${id.slice(0, 8)}.md`,
        title: content.match(/^# (.+)/)?.[1] ?? "Quick Thought",
        markdown: `---\nid: ${id}\n---\n\n${content}`,
        revision: '"r1"',
      };
      notes.set(id, note);
      return respond(note, 201);
    }
    if (path === "v1/notes" && method === "POST") {
      const id = String(body.id ?? crypto.randomUUID());
      let name = String(body.name);
      if (!name.endsWith(".md")) name += ".md";
      const target = [body.folderPath, name].filter(Boolean).join("/");
      if (Array.from(notes.values()).some((n) => n.path === target))
        return respond({}, 409);
      const note = {
        id,
        path: target,
        title: String(body.title ?? name.replace(/\.md$/, "")),
        markdown: String(
          body.initialMarkdown ??
            `---\nid: ${id}\n---\n\n# ${name.replace(/\.md$/, "")}\n`,
        ),
        revision: '"r1"',
      };
      notes.set(id, note);
      return respond(note, 201);
    }
    if (path === "v1/folders") {
      const target = [body.parentPath, body.name].filter(Boolean).join("/");
      if (folders.has(target)) return respond({}, 409);
      folders.add(target);
      return respond({ path: target, isDirectory: true });
    }
    if (path === "v1/library/item") {
      const target = url.searchParams.get("path")!;
      return respond({
        path: target,
        isDirectory: folders.has(target),
        descendantCount: Array.from(notes.values()).filter((n) =>
          n.path.startsWith(target + "/"),
        ).length,
      });
    }
    if (path.startsWith("v1/library/")) {
      const action = path.split("/").at(-1);
      const source = String(body.sourcePath ?? body.path);
      const note = Array.from(notes.values()).find((n) => n.path === source);
      if (action === "delete") {
        if (note) notes.delete(note.id);
        else {
          folders.delete(source);
          for (const [id, n] of notes)
            if (n.path.startsWith(source + "/")) notes.delete(id);
        }
        return respond({ path: source, isDirectory: !note, affectedItems: 1 });
      }
      const dest =
        action === "rename"
          ? [
              source.includes("/")
                ? source.slice(0, source.lastIndexOf("/"))
                : "",
              String(body.newName),
            ]
              .filter(Boolean)
              .join("/")
          : [body.destinationFolderPath, source.split("/").at(-1)]
              .filter(Boolean)
              .join("/");
      if (!note) {
        folders.delete(source);
        folders.add(dest);
        for (const [id, n] of notes)
          if (n.path.startsWith(source + "/"))
            notes.set(id, { ...n, path: dest + n.path.slice(source.length) });
        return respond({ path: dest, isDirectory: true });
      }
      const target =
        action === "copy" &&
        Array.from(notes.values()).some((n) => n.path === dest)
          ? dest.replace(/\.md$/, " copy.md")
          : dest;
      if (
        action !== "copy" &&
        Array.from(notes.values()).some(
          (n) => n.path === target && n.id !== note.id,
        )
      )
        return respond({}, 409);
      const id = action === "copy" ? crypto.randomUUID() : note.id;
      notes.set(id, {
        ...note,
        id,
        path: target,
        title: target.split("/").at(-1)!.replace(/\.md$/, ""),
      });
      return respond({ path: target, id, isDirectory: false });
    }
    if (path === "v1/assets" && method === "POST") {
      const id = url.searchParams.get("id")!;
      const filename = url.searchParams.get("filename")!;
      const meta = {
        id,
        originalFilename: filename,
        contentType: req.headers()["content-type"],
        byteSize: req.postDataBuffer()?.length ?? 0,
        extension: "." + filename.split(".").at(-1),
        inlineImage: req.headers()["content-type"].startsWith("image/"),
      };
      assets.set(id, { meta, bytes: req.postDataBuffer() ?? Buffer.alloc(0) });
      assetCounter++;
      return respond(meta, 201);
    }
    if (path.startsWith("v1/assets/")) {
      const id = path.split("/")[2],
        asset = assets.get(id);
      if (!asset) return respond({}, 404);
      if (path.endsWith("/metadata")) return respond(asset.meta);
      return route.fulfill({
        status: 200,
        contentType: asset.meta.contentType,
        body: asset.bytes,
      });
    }
    const match = path.match(/^v1\/notes\/([^/]+)(.*)$/);
    if (match) {
      const n = notes.get(match[1]);
      if (!n) return respond({}, 404);
      if (match[2] === "/links") {
        const target = notes.get(n.id === first ? second : first);
        return respond({
          noteId: n.id,
          outgoing: target
            ? [
                {
                  raw: `[[${target.title}]]`,
                  target: target.title,
                  state: "resolved",
                  targetId: target.id,
                  targetTitle: target.title,
                  targetPath: target.path,
                  kind: "wiki",
                },
              ]
            : [],
          backlinks: target
            ? [
                {
                  sourceId: target.id,
                  sourceTitle: target.title,
                  sourcePath: target.path,
                },
              ]
            : [],
        });
      }
      if (match[2] === "/history")
        return respond([
          {
            commit: "a".repeat(40),
            timestamp: "2026-09-28T10:00:00Z",
            message: "Document library structure",
            author: "Slate",
          },
        ]);
      if (match[2].startsWith("/history/"))
        return respond({
          ...n,
          commit: "a".repeat(40),
          timestamp: "2026-09-28T10:00:00Z",
          message: "Document library structure",
          markdown: "# Earlier version\n\nOriginal design notes.",
        });
      if (match[2] === "/related") {
        const target = notes.get(n.id === first ? second : first)!;
        return respond([
          {
            id: target.id,
            title: target.title,
            path: target.path,
            score: 0.82,
            reasons: ["Links to this note", "Shares the library topic"],
          },
        ]);
      }
      if (match[2] === "/graph") {
        const target = notes.get(n.id === first ? second : first)!;
        return respond({
          focus: n.id,
          nodes: [
            {
              id: n.id,
              title: n.title,
              path: n.path,
              type: "reference",
              status: null,
              depth: 0,
            },
            {
              id: target.id,
              title: target.title,
              path: target.path,
              type: "reading",
              status: null,
              depth: 1,
            },
          ],
          edges: [{ source: n.id, target: target.id }],
          limited: false,
        });
      }
      if (match[2] === "/wiki-export" && method === "GET") {
        const proposedMarkdown = n.markdown.replace(
          "[[Reading list]]",
          "[Reading list](../Research/Reading%20list.md)",
        );
        return respond({
          id: n.id,
          path: n.path,
          revision: n.revision,
          originalMarkdown: n.markdown,
          proposedMarkdown,
          changes:
            proposedMarkdown === n.markdown
              ? []
              : [
                  {
                    start: n.markdown.indexOf("[[Reading list]]"),
                    length: "[[Reading list]]".length,
                    original: "[[Reading list]]",
                    proposed: "[Reading list](../Research/Reading%20list.md)",
                    targetId: second,
                    targetPath: "Research/Reading list.md",
                  },
                ],
        });
      }
      if (match[2] === "/wiki-export" && method === "POST") {
        if (body.sourceRevision !== n.revision) return respond({}, 412);
        const updated = {
          ...n,
          markdown: String(body.proposedMarkdown),
          revision: '"wiki-export"',
        };
        notes.set(n.id, updated);
        return respond(updated);
      }
      if (method === "PUT") {
        if (conflict || req.headers()["if-match"] !== n.revision)
          return respond({}, 412);
        const updated = {
          ...n,
          markdown: String(body.markdown),
          revision: '"r' + Date.now() + '"',
        };
        notes.set(n.id, updated);
        return respond(updated);
      }
      return respond(n);
    }
    return respond({}, 404);
  });
  return {
    notes,
    folders,
    requests,
    askRequests,
    projectRequests,
    evolutionRequests,
    setConflict: (value: boolean) => {
      conflict = value;
    },
    uploads: () => assetCounter,
  };
}
export async function connect(page: Page) {
  await page.goto("/");
  await page.getByLabel("Server URL").fill("https://fixture.slate.test");
  await page
    .getByLabel("Device token", { exact: true })
    .fill("fixture-token-not-a-secret");
  await page.getByLabel("Remember this device").check();
  await page
    .getByRole("button", { name: "Test connection & open library" })
    .click();
  await page.getByRole("heading", { name: "Library", exact: true }).waitFor();
}
export async function openNote(page: Page) {
  await page
    .getByRole("treeitem", { name: "Projects", exact: true })
    .first()
    .click();
  await page
    .getByRole("treeitem", { name: "Library architecture", exact: true })
    .first()
    .click();
  await page
    .getByRole("heading", { name: "Library architecture", exact: true })
    .first()
    .waitFor();
}
